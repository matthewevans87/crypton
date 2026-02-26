#!/usr/bin/env python3
import urllib.parse
import hashlib
import hmac
import base64

# Get all asset balances.

import http.client
import urllib.request
import urllib.parse
import hashlib
import hmac
import base64
import json
import time


def main():
    response = request(
        method="POST",
        path="/0/private/Balance",
        public_key="9UA5+IEHP68a8i7pwRRiahubuj14J/LIfs05vhupHyFbT7GWyFs9gXr1",
        private_key="bZI23Z8QfS1PhQjm/hynRtZmCVv+IaITiU8f9pBwKOlcy1w14jDRkdOs9pe4wlYpPRXKHyVxCZX3z1wHrG2zOw==",
        environment="https://api.kraken.com",
    )
    print(response.read().decode())


def request(
    method: str = "GET",
    path: str = "",
    query: dict | None = None,
    body: dict | None = None,
    public_key: str = "",
    private_key: str = "",
    environment: str = "",
) -> http.client.HTTPResponse:
    url = environment + path
    query_str = ""
    if query is not None and len(query) > 0:
        query_str = urllib.parse.urlencode(query)
        url += "?" + query_str
    nonce = ""
    if len(public_key) > 0:
        if body is None:
            body = {}
        nonce = body.get("nonce")
        if nonce is None:
            nonce = get_nonce()
            body["nonce"] = nonce
    headers = {}
    body_str = ""
    if body is not None and len(body) > 0:
        body_str = json.dumps(body)
        headers["Content-Type"] = "application/json"
    if len(public_key) > 0:
        headers["API-Key"] = public_key
        headers["API-Sign"] = get_signature(
            private_key, query_str + body_str, nonce, path
        )

    # DEBUG
    print(f"DEBUG: nonce={nonce}, body_str={body_str}, query_str={query_str}")
    print(f"DEBUG: signature={headers.get('API-Sign')}")

    req = urllib.request.Request(
        method=method,
        url=url,
        data=body_str.encode(),
        headers=headers,
    )
    return urllib.request.urlopen(req)


def get_nonce() -> str:
    return str(int(time.time() * 1000))


def get_signature(private_key: str, data: str, nonce: str, path: str) -> str:
    print(
        f"DEBUG get_signature: private_key={private_key[:10]}..., data={repr(data)}, nonce={nonce}, path={path}"
    )
    result = sign(
        private_key=private_key,
        message=path.encode() + hashlib.sha256((nonce + data).encode()).digest(),
    )
    print(f"DEBUG get_signature result: {result}")
    return result


def sign(private_key: str, message: bytes) -> str:
    return base64.b64encode(
        hmac.new(
            key=base64.b64decode(private_key),
            msg=message,
            digestmod=hashlib.sha512,
        ).digest()
    ).decode()


if __name__ == "__main__":
    main()


# def get_kraken_signature(urlpath, data, secret):

#     if isinstance(data, str):
#         encoded = (str(json.loads(data)["nonce"]) + data).encode()
#     else:
#         encoded = (str(data["nonce"]) + urllib.parse.urlencode(data)).encode()
#     message = urlpath.encode() + hashlib.sha256(encoded).digest()

#     mac = hmac.new(base64.b64decode(secret), message, hashlib.sha512)
#     sigdigest = base64.b64encode(mac.digest())
#     return sigdigest.decode()

# api_sec = "kQH5HW/8p1uGOVjbgWA7FunAmGO8lsSUXNsu3eow76sz84Q18fWxnyRzBHCd3pd5nE9qa99HAZtuZuj6F1huXg=="

# payload = {
#   "nonce": 1616492376594,
# }

# signature = get_kraken_signature("/0/private/GetCustodyTask?id=TGWOJ4JQPOTZT2", payload, api_sec)
# print("API-Sign: {}".format(signature))
