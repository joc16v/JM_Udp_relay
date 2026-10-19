Fixes in v1.1.1:

- Explicit **Delete selected row** button and visible row headers.
- Completely blank rows no longer prevent Start/Save; incomplete rows identify the row to correct.
- Blank rows are removed on Start so live checkbox switches stay aligned with their destinations.
- Added form-level regression checks for row deletion, blank rows, incomplete rows and preserved Send states; CI builds the complete Visual Studio solution.

Windows x64 UDP relay with one input and multiple destinations.

- Live **Send** checkbox per destination: switch forwarding on/off without stopping the input or other routes.
- Saved IP/port settings and checkbox states; optional start when the app opens.
- Byte-for-byte UDP payload forwarding; no stYpe protocol parsing.
- Receive rate, packet counters and error status.
- Portable package includes .NET runtime and source code.

Download **StypeUdpRelay_v1.1.1_Windows_x64.zip** under Assets, extract the whole archive and run `app/StypeUdpRelay.exe`. The automatic GitHub Source code ZIP contains source only.

Defaults: input `0.0.0.0:6299`, outputs `10.97.17.39:6301` and `10.97.17.48:6301`.

Studio testing with actual stYpe/Viz equipment and latency measurement remain to be completed.
