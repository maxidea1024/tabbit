// Nothing but an import.
//
// Dart has no compile-only mode that resolves an unattached library: `dart analyze`
// without a package config cannot even find `int`. Running a program that imports the
// generated library does resolve it, and a name that does not compile fails here.

import 'tables.dart';

void main() {
  // Referenced so the import cannot be dropped as unused.
  print(Tables);
}
