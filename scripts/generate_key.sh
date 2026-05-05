#!/bin/bash
# Generate a shared AES-256 key for TrpgVoiceDigest upload service <-> server communication.
# Copy the output into both the Upload (appsettings.json) and Server (appsettings.json) configs.

KEY=$(openssl rand -base64 32)
echo "SharedSecret: $KEY"
echo ""
echo "Add this to:"
echo "  1. src/TrpgVoiceDigest.Upload/appsettings.json  -> \"SharedSecret\": \"$KEY\""
echo "  2. src/TrpgVoiceDigest.Server/appsettings.json   -> \"SharedSecret\": \"$KEY\""
