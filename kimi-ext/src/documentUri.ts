import { pathToFileURL } from 'node:url';
import type { Uri } from 'vscode';

export function toProtocolUri(uri: Pick<Uri, 'scheme' | 'fsPath' | 'toString'>): string {
  // VS Code escapes a Windows drive colon as %3A. System.Uri.LocalPath then
  // produces /c:/... instead of a Windows path. Preserve the drive separator
  // while still escaping spaces, Unicode, percent signs and fragment markers.
  return uri.scheme === 'file' ? pathToFileURL(uri.fsPath).href : uri.toString();
}
