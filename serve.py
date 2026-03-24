#!/usr/bin/env python3
"""
Simple proxy server for MikroTik Address List config UI.
Serves config-ui.html and proxies Technitium API requests.

Usage: python3 serve.py [port]
Then open: http://localhost:8080/config-ui.html?url=http://localhost:8080
"""

import http.server
import urllib.request
import urllib.error
import sys
import os

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8080
DIR = os.path.dirname(os.path.abspath(__file__))


class ProxyHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=DIR, **kwargs)

    def do_GET(self):
        if self.path.startswith('/api/'):
            self.proxy_request('GET')
        else:
            super().do_GET()

    def do_POST(self):
        if self.path.startswith('/api/'):
            self.proxy_request('POST')
        else:
            self.send_error(405)

    def proxy_request(self, method):
        # Read target URL from Referer or default
        target = self.headers.get('X-Target', '').rstrip('/')
        if not target:
            self.send_error(400, 'Missing X-Target header')
            return

        url = target + self.path

        try:
            body = None
            if method == 'POST':
                length = int(self.headers.get('Content-Length', 0))
                body = self.rfile.read(length) if length else None

            req = urllib.request.Request(url, data=body, method=method)
            req.add_header('Content-Type', self.headers.get('Content-Type', 'application/x-www-form-urlencoded'))

            with urllib.request.urlopen(req, timeout=15) as resp:
                data = resp.read()
                self.send_response(resp.status)
                self.send_header('Content-Type', resp.headers.get('Content-Type', 'application/json'))
                self.send_header('Content-Length', len(data))
                self.end_headers()
                self.wfile.write(data)

        except urllib.error.HTTPError as e:
            data = e.read()
            self.send_response(e.code)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', len(data))
            self.end_headers()
            self.wfile.write(data)
        except Exception as e:
            msg = str(e).encode()
            self.send_response(502)
            self.send_header('Content-Length', len(msg))
            self.end_headers()
            self.wfile.write(msg)

    def log_message(self, format, *args):
        print(f"[{self.log_date_time_string()}] {format % args}")


if __name__ == '__main__':
    print(f"Serving on http://localhost:{PORT}")
    print(f"Open: http://localhost:{PORT}/config-ui.html")
    http.server.HTTPServer(('', PORT), ProxyHandler).serve_forever()
