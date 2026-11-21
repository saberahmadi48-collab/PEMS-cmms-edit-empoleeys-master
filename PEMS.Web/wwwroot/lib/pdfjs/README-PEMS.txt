pdf.js (pdfjs-dist) 4.0.379 - vendored for the offline PEMS product (SCRUM-20 / plan row #42)

Used by: Views/EngineeringDocuments/Details.cshtml and Views/VDR/Details.cshtml through wwwroot/js/pdfjs-loader.js.
Source:   npm package pdfjs-dist@4.0.379 (https://registry.npmjs.org/pdfjs-dist/-/pdfjs-dist-4.0.379.tgz), build/ folder.
Licence:  Apache-2.0 (file LICENSE next to this one), Mozilla Foundation. Listed in the third-party inventory (plan row #121).
Files:    pdf.min.mjs        sha256 61c2ab9c464aabd52ae06ed7d175949c78f2b476cf477cab55a80ae9a6ce12d2
          pdf.worker.min.mjs sha256 c0408d13d3a128743ea53b0162205487c27ad8312bef6b97eb5b4a07e27395c3
Notes:    4.x is published as ES modules only, so the viewers load the library through the module pdfjs-loader.js, which sets
          window.pdfjsLib and the worker path (relative to the loader, so a virtual directory works). The sourceMappingURL
          comments in the files point to .map files that are deliberately not shipped.
Upgrade:  replace both files from the new package version, update the version and hashes here, run OfflineAssetsTests and
          the README check M-17.
