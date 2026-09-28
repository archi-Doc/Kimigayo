"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.toProtocolUri = toProtocolUri;
const node_url_1 = require("node:url");
function toProtocolUri(uri) {
    // VS Code escapes a Windows drive colon as %3A. System.Uri.LocalPath then
    // produces /c:/... instead of a Windows path. Preserve the drive separator
    // while still escaping spaces, Unicode, percent signs and fragment markers.
    return uri.scheme === 'file' ? (0, node_url_1.pathToFileURL)(uri.fsPath).href : uri.toString();
}
//# sourceMappingURL=documentUri.js.map