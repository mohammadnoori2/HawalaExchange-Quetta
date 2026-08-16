Export PDF native setup for DinkToPdf

This project uses DinkToPdf (a .NET wrapper around wkhtmltopdf) to generate PDFs server-side.

Requirements (for Windows x64 development):

1. Download the native wkhtmltox library (wkhtmltopdf) for Windows x64. You can get binaries from the official project or community builds. Example sources:
   - https://github.com/wkhtmltopdf/packaging/releases
   - https://wkhtmltopdf.org/downloads.html

2. From the downloaded package, copy the native DLL (e.g., `libwkhtmltox.dll` or appropriate Win64 DLL) into the folder:
   `HawalaExchange.Web\runtimes\win-x64\native\`

   For 32-bit Windows use the `runtimes\win-x86\native\` folder instead.

3. The project is configured to copy any files under `runtimes/*/native/` to the output during build/publish. After placing the native DLL there, run the app and DinkToPdf should be able to find the native library.

Notes:
- If you deploy to Linux, download the appropriate native package for the target distro and place it under the corresponding `runtimes/<rid>/native/` folder, or configure your server to install wkhtmltopdf system-wide.
- If you prefer, I can add the actual DLL into the repository (if you confirm licensing is acceptable) or automate downloading it into the runtimes folder during build.
