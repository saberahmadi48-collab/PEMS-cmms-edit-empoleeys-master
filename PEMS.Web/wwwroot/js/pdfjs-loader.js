// Loads the vendored pdf.js (PEMS.Web/wwwroot/lib/pdfjs, version 4.0.379, Apache-2.0) for the PDF viewers of
// Engineering Documents and VDR (SCRUM-20 / #42: the product is offline, no CDN). pdf.js 4 is published as ES modules,
// so this file is a module: it sets the global `pdfjsLib` that the viewer scripts use. Paths are relative to this
// file, so the app also works under a virtual directory.
import * as pdfjsLib from '../lib/pdfjs/pdf.min.mjs';

pdfjsLib.GlobalWorkerOptions.workerSrc = new URL('../lib/pdfjs/pdf.worker.min.mjs', import.meta.url).href;
window.pdfjsLib = pdfjsLib;
