#!/bin/sh
set -eu

if [ -s /certificates/signing.pfx ] && [ -s /certificates/encryption.pfx ]; then
  echo "OpenIddict certificates already exist."
  exit 0
fi

apk add --no-cache openssl >/dev/null
umask 077

create_certificate() {
  name="$1"
  purpose="$2"
  openssl req -x509 -newkey rsa:3072 -sha256 -nodes \
    -keyout "/tmp/${name}.key" \
    -out "/tmp/${name}.crt" \
    -days 3650 \
    -subj "/CN=PassingTrace ${purpose}"
  openssl pkcs12 -export \
    -out "/certificates/${name}.pfx" \
    -inkey "/tmp/${name}.key" \
    -in "/tmp/${name}.crt" \
    -passout "pass:${CERTIFICATE_PASSWORD}"
  rm -f "/tmp/${name}.key" "/tmp/${name}.crt"
}

create_certificate signing Signing
create_certificate encryption Encryption
echo "OpenIddict certificates generated."
