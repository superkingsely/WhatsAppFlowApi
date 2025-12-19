#!/bin/bash

# Upload public key to Meta WhatsApp Graph API
# Usage: ./upload_public_key.sh YOUR_PERMANENT_TOKEN

set -e

if [ -z "$1" ]; then
  echo "Usage: $0 YOUR_PERMANENT_TOKEN"
  exit 1
fi

PERM_TOKEN="$1"
PHONE_ID="834043419801889"

# Read PEM file and escape newlines for JSON
PEM_CONTENT=$(cat public_key.pem | sed ':a;N;$!ba;s/\n/\\n/g')

# Build JSON body
JSON_BODY=$(cat <<EOF
{"business_public_key":"$PEM_CONTENT"}
EOF
)

echo "Uploading public key to Meta..."
echo "Phone ID: $PHONE_ID"
echo ""

# Send request
curl -i -X POST "https://graph.facebook.com/v22.0/$PHONE_ID/whatsapp_business_encryption" \
  -H "Authorization: Bearer $PERM_TOKEN" \
  -H "Content-Type: application/json" \
  -d "$JSON_BODY"

echo ""
echo "Done!"
