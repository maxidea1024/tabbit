import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
  TransportKind,
} from 'vscode-languageclient/node';

let client: LanguageClient | undefined;

/**
 * Starts the server the tool itself provides.
 *
 * Nothing here judges a `.tbs` file. `tabbit lsp` runs the same parser a conversion runs, so
 * what this editor underlines and what a build refuses are one answer - which is the whole
 * reason the server is a subcommand of the tool rather than a program of its own.
 */
export function activate(context: vscode.ExtensionContext): void {
  const command = findServer();

  if (!command) {
    void vscode.window.showErrorMessage(
      'Tabbit: no `tabbit` executable found. Set `tabbit.path` in your settings, or put ' +
        'tabbit on your PATH.',
    );
    return;
  }

  const messages = vscode.workspace.getConfiguration('tabbit').get<string>('messages', '');
  const args = messages ? ['lsp', '--messages', messages] : ['lsp'];

  const serverOptions: ServerOptions = {
    run: { command, args, transport: TransportKind.stdio },
    debug: { command, args, transport: TransportKind.stdio },
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: 'file', language: 'tbs' }],

    // A `.tbs` changed outside the editor - a branch switch, another tool - still changes what
    // the file being edited resolves against, because the whole folder is checked as one set.
    synchronize: {
      fileEvents: vscode.workspace.createFileSystemWatcher('**/*.tbs'),
    },
  };

  client = new LanguageClient('tabbit', 'Tabbit Language Server', serverOptions, clientOptions);
  context.subscriptions.push(client);

  void client.start();
}

export function deactivate(): Thenable<void> | undefined {
  return client?.stop();
}

/**
 * Where to find the tool, in the order somebody would look.
 *
 * The setting first, because it is the answer for a machine that has several; then the PATH,
 * which is where an installed copy is; then this repository's own build output, so that
 * working on the tool needs no setting at all.
 */
function findServer(): string | undefined {
  const configured = vscode.workspace.getConfiguration('tabbit').get<string>('path', '').trim();

  if (configured) {
    return fs.existsSync(configured) ? configured : undefined;
  }

  const onPath = searchPath();

  if (onPath) {
    return onPath;
  }

  return searchBuildOutput();
}

function executableNames(): string[] {
  return process.platform === 'win32' ? ['tabbit.exe', 'tabbit'] : ['tabbit'];
}

function searchPath(): string | undefined {
  const directories = (process.env.PATH ?? '').split(path.delimiter).filter(Boolean);

  for (const directory of directories) {
    for (const name of executableNames()) {
      const candidate = path.join(directory, name);

      if (fs.existsSync(candidate)) {
        return candidate;
      }
    }
  }

  return undefined;
}

/** The build output of a checkout of the tool, for whoever is working on the tool. */
function searchBuildOutput(): string | undefined {
  for (const folder of vscode.workspace.workspaceFolders ?? []) {
    for (const configuration of ['Debug', 'Release']) {
      for (const name of executableNames()) {
        const candidate = path.join(
          folder.uri.fsPath, 'src', 'bin', configuration, 'net10.0', name);

        if (fs.existsSync(candidate)) {
          return candidate;
        }
      }
    }
  }

  return undefined;
}
