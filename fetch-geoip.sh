#!/bin/sh
# Download the IP2Location country database for use by the game server

####
# This file must stay /bin/sh and POSIX compliant for macOS and BSD portability.
# Copy-paste the entire script into https://shellcheck.net to check.
####

set -o errexit || exit $?

# Set the working directory to the location of this script
HERE=$(dirname "$0")
cd "${HERE}"

GEOIP_FILE="IP2LOCATION-LITE-DB1.IPV6.BIN.ZIP"
GEOIP_URL="https://github.com/OpenRA/GeoIP-Database/releases/download/monthly/IP2LOCATION-LITE-DB1.IPV6.BIN.ZIP"
TIMEOUT_SECONDS=30

# Database does not exist or is older than 30 days.
if [ -z "$(find . -path ./${GEOIP_FILE} -mtime -30 -print)" ]; then
	rm -f "${GEOIP_FILE}" || :
	echo "Downloading IP2Location GeoIP database (timeout: ${TIMEOUT_SECONDS}s)..."
	
	DOWNLOAD_SUCCESS=0
	
	if command -v curl >/dev/null 2>&1; then
		if curl --connect-timeout ${TIMEOUT_SECONDS} --max-time $((TIMEOUT_SECONDS * 2)) -s -L -o "${GEOIP_FILE}" "${GEOIP_URL}" 2>/dev/null; then
			DOWNLOAD_SUCCESS=1
		fi
	elif command -v wget >/dev/null 2>&1; then
		if wget --timeout=${TIMEOUT_SECONDS} -q -O "${GEOIP_FILE}" "${GEOIP_URL}" 2>/dev/null; then
			DOWNLOAD_SUCCESS=1
		fi
	fi
	
	if [ "${DOWNLOAD_SUCCESS}" -eq 0 ]; then
		echo "Warning: GeoIP database download failed or timed out."
		echo "         Server will run without GeoIP country lookup."
		echo "         You can manually download from: ${GEOIP_URL}"
		rm -f "${GEOIP_FILE}" || :
	fi
fi
# changeCR2LF