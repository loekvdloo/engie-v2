#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${1:-engie}"

oc project "${NAMESPACE}"

oc start-build engie-mca-event-handler --from-dir=. --follow
oc start-build engie-mca-message-processor --from-dir=. --follow
oc start-build engie-mca-message-validator --from-dir=. --follow
oc start-build engie-mca-nack-handler --from-dir=. --follow
oc start-build engie-mca-output-handler --from-dir=. --follow
