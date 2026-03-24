/*
MikroTik Address List DNS App for Technitium DNS Server
Exports resolved DNS IPs to MikroTik firewall address-list via REST API.

License: GPL-3.0
*/

using DnsServerCore.ApplicationCommon;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TechnitiumLibrary;
using TechnitiumLibrary.Net.Dns;
using TechnitiumLibrary.Net.Dns.ResourceRecords;

namespace MikroTikAddressList
{
    public sealed class App : IDnsApplication, IDnsPostProcessor
    {
        #region variables

        IDnsServer _dnsServer;

        bool _enabled;
        string _mikrotikUrl;
        string _mikrotikUsername;
        string _mikrotikPassword;
        bool _useTtlAsTimeout;
        string _defaultTimeout;
        bool _enableIPv6;
        bool _waitForMikrotik;
        bool _skipCertificateCheck;

        // domain -> addressListName mapping
        Dictionary<string, string> _domainToList;

        HttpClient _httpClient;

        // Track recently sent IPs to avoid hammering MikroTik with duplicates
        readonly ConcurrentDictionary<string, DateTime> _recentlySent = new ConcurrentDictionary<string, DateTime>();
        static readonly TimeSpan DEDUP_INTERVAL = TimeSpan.FromSeconds(30);

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_httpClient != null)
            {
                _httpClient.Dispose();
                _httpClient = null;
            }
        }

        #endregion

        #region private

        private static string GetParentZone(string domain)
        {
            int i = domain.IndexOf('.');
            if (i > -1)
                return domain.Substring(i + 1);

            return null;
        }

        private string GetMatchedListName(string domain)
        {
            domain = domain.TrimEnd('.').ToLowerInvariant();

            do
            {
                if (_domainToList.TryGetValue(domain, out string listName))
                    return listName;

                domain = GetParentZone(domain);
            }
            while (domain != null);

            return null;
        }

        private async Task SendToMikroTikAsync(string ipAddress, string domain, uint ttl, bool isIPv6, string listName)
        {
            // Deduplication: skip if we sent this IP recently
            string dedupKey = ipAddress + "|" + listName;

            if (_recentlySent.TryGetValue(dedupKey, out DateTime lastSent))
            {
                if (DateTime.UtcNow - lastSent < DEDUP_INTERVAL)
                    return;
            }

            _recentlySent[dedupKey] = DateTime.UtcNow;

            try
            {
                // Determine timeout
                string timeout;
                if (_useTtlAsTimeout && ttl > 0)
                    timeout = TimeSpan.FromSeconds(ttl).ToString();
                else
                    timeout = _defaultTimeout;

                // Build request body
                var body = new Dictionary<string, string>
                {
                    { "list", listName },
                    { "address", ipAddress },
                    { "comment", "Technitium: " + domain },
                    { "timeout", timeout }
                };

                string jsonBody = JsonSerializer.Serialize(body);
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                // IPv4: PUT /rest/ip/firewall/address-list
                // IPv6: PUT /rest/ipv6/firewall/address-list
                string restPath = isIPv6 ? "/rest/ipv6/firewall/address-list" : "/rest/ip/firewall/address-list";
                string url = _mikrotikUrl.TrimEnd('/') + restPath;

                HttpResponseMessage response = await _httpClient.PutAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    _dnsServer.WriteLog("[MikroTikAddressList] Added " + ipAddress + " (" + domain + ") to list '" + listName + "' timeout=" + timeout);
                }
                else
                {
                    string responseBody = await response.Content.ReadAsStringAsync();

                    // MikroTik returns 400 with "failure: already have such entry"
                    // when the IP already exists — this is expected, not an error
                    if (responseBody.Contains("already have such entry", StringComparison.OrdinalIgnoreCase))
                    {
                        // Entry already exists — silently skip
                        return;
                    }

                    _dnsServer.WriteLog("[MikroTikAddressList] Error adding " + ipAddress + " to MikroTik: HTTP " + (int)response.StatusCode + " " + responseBody);
                }
            }
            catch (Exception ex)
            {
                _dnsServer.WriteLog("[MikroTikAddressList] Exception sending to MikroTik: " + ex.Message);
            }
        }

        private void CleanupRecentlySent()
        {
            // Periodically clean up old dedup entries
            DateTime cutoff = DateTime.UtcNow - DEDUP_INTERVAL;
            List<string> toRemove = new List<string>();

            foreach (var kvp in _recentlySent)
            {
                if (kvp.Value < cutoff)
                    toRemove.Add(kvp.Key);
            }

            foreach (string key in toRemove)
                _recentlySent.TryRemove(key, out _);
        }

        #endregion

        #region public

        public Task InitializeAsync(IDnsServer dnsServer, string config)
        {
            _dnsServer = dnsServer;

            using JsonDocument jsonDocument = JsonDocument.Parse(config);
            JsonElement jsonConfig = jsonDocument.RootElement;

            _enabled = jsonConfig.GetPropertyValue("enabled", true);
            _mikrotikUrl = jsonConfig.GetPropertyValue("mikrotikUrl", "https://192.168.88.1");
            _mikrotikUsername = jsonConfig.GetPropertyValue("mikrotikUsername", "admin");
            _mikrotikPassword = jsonConfig.GetPropertyValue("mikrotikPassword", "");
            _useTtlAsTimeout = jsonConfig.GetPropertyValue("useTtlAsTimeout", true);
            _defaultTimeout = jsonConfig.GetPropertyValue("defaultTimeout", "00:05:00");
            _enableIPv6 = jsonConfig.GetPropertyValue("enableIPv6", false);
            _waitForMikrotik = jsonConfig.GetPropertyValue("waitForMikrotik", false);
            _skipCertificateCheck = jsonConfig.GetPropertyValue("skipCertificateCheck", true);

            // Parse domain-to-list mappings
            _domainToList = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // "domainLists" maps list names to domain arrays
            if (jsonConfig.TryGetProperty("domainLists", out JsonElement jsonDomainLists))
            {
                foreach (JsonProperty prop in jsonDomainLists.EnumerateObject())
                {
                    string listName = prop.Name;
                    foreach (JsonElement jsonDomain in prop.Value.EnumerateArray())
                    {
                        string domain = jsonDomain.GetString();
                        if (!string.IsNullOrWhiteSpace(domain))
                            _domainToList[domain.Trim().TrimEnd('.').ToLowerInvariant()] = listName;
                    }
                }
            }

            // Create HttpClient with Basic Auth
            if (_httpClient != null)
            {
                _httpClient.Dispose();
                _httpClient = null;
            }

            HttpClientHandler handler = new HttpClientHandler();

            if (_skipCertificateCheck)
            {
                handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
            }

            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(10);

            string authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(_mikrotikUsername + ":" + _mikrotikPassword));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);

            // Clear dedup cache on config reload
            _recentlySent.Clear();

            _dnsServer.WriteLog("[MikroTikAddressList] Initialized. Monitoring " + _domainToList.Count + " domain(s). MikroTik: " + _mikrotikUrl);

            return Task.CompletedTask;
        }

        public async Task<DnsDatagram> PostProcessAsync(DnsDatagram request, IPEndPoint remoteEP, DnsTransportProtocol protocol, DnsDatagram response)
        {
            try
            {
                if (!_enabled)
                    return response;

                if (response == null || response.Answer == null || response.Answer.Count == 0)
                    return response;

                if (response.RCODE != DnsResponseCode.NoError)
                    return response;

                DnsQuestionRecord question = request.Question[0];
                string queriedDomain = question.Name.TrimEnd('.');

                string matchedList = GetMatchedListName(queriedDomain);
                if (matchedList == null)
                    return response;

                // Periodically clean up dedup cache
                CleanupRecentlySent();

                // Extract IPs from answer records
                List<Task> tasks = new List<Task>();

                foreach (DnsResourceRecord record in response.Answer)
                {
                    switch (record.Type)
                    {
                        case DnsResourceRecordType.A:
                            {
                                DnsARecordData aRecord = record.RDATA as DnsARecordData;
                                if (aRecord != null)
                                {
                                    string ip = aRecord.Address.ToString();
                                    tasks.Add(SendToMikroTikAsync(ip, queriedDomain, record.TTL, false, matchedList));
                                }
                            }
                            break;

                        case DnsResourceRecordType.AAAA:
                            {
                                if (!_enableIPv6)
                                    break;

                                DnsAAAARecordData aaaaRecord = record.RDATA as DnsAAAARecordData;
                                if (aaaaRecord != null)
                                {
                                    string ip = aaaaRecord.Address.ToString();
                                    tasks.Add(SendToMikroTikAsync(ip, queriedDomain, record.TTL, true, matchedList));
                                }
                            }
                            break;
                    }
                }

                if (tasks.Count > 0)
                {
                    if (_waitForMikrotik)
                    {
                        // Wait for MikroTik before returning DNS response
                        try
                        {
                            await Task.WhenAll(tasks);
                        }
                        catch (Exception ex)
                        {
                            _dnsServer.WriteLog("[MikroTikAddressList] Error: " + ex.Message);
                        }
                    }
                    else
                    {
                        // Fire and forget — don't block DNS response
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.WhenAll(tasks);
                            }
                            catch (Exception ex)
                            {
                                _dnsServer.WriteLog("[MikroTikAddressList] Error: " + ex.Message);
                            }
                        });
                    }
                }

                return response;
            }
            catch (Exception ex)
            {
                _dnsServer.WriteLog("[MikroTikAddressList] Exception: " + ex.Message);
                return response;
            }
        }

        #endregion

        #region properties

        public string Description
        { get { return "Exports resolved DNS IPs for configured domains to MikroTik firewall address-list via REST API."; } }

        #endregion
    }
}
