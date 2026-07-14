# SCN-002 — Single Critical Camera Offline

## Preconditions

Gateway and internet are healthy. At least two cameras are monitored and healthy. Away mode is active.

## Action

Disconnect power or Ethernet from one selected camera.

## Expected behavior

- Target transitions healthy → suspect → offline using method-aware confirmation.
- Other targets, gateway, and internet remain healthy.
- Incident class is `critical_device_offline`, not `property_unreachable`.
- Alert reports the camera as unreachable and lists power/cable/Wi-Fi/switch/device failure as possibilities.
- Restoration requires stable successful checks and sends one linked update.

## Prohibited outcomes

- “Tampering confirmed.”
- Property-wide outage incident.
- Repeated SMS for every failed probe.
