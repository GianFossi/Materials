# Release and Safety Notes

## Release and safety notes | Note di rilascio e sicurezza

- The repository test fixture `tests/Fixtures/asme_materials.working.db` is a read-only copy of the
  supplied working database. Tests must copy it to a temporary path before any mutation; never edit
  the fixture in place.

- Opening `reference.db` creates `reference.working.db`; the selected reference file is never written.
- Modified working databases are persisted/exported only through **Save Working Copy As...**.
- Automatic backups are created beside the working database before destructive SQL/schema operations.
- The backup manager retains the newest generated backups; backup files should be included in release
  support instructions, not copied into source control.
- Raw-table undo/redo is transaction-based and persisted for supported rowid and primary-key tables.
- SQL/schema undo is restricted to recognized `CREATE TABLE`, `CREATE INDEX`, and table-rename forms;
  arbitrary SQL remains audited but is not automatically reversible.
- Foreign-key checks run before raw-table commits and before publishing.
- Excel publishing produces packed `.xll` add-ins for both 32-bit and 64-bit Excel targets. The
  packaged add-in contains managed dependencies and the SQLite native runtime; it must be tested on
  the installed Excel bitness before deployment.
- Native cancellation of an already-running SQLite command remains dependent on provider support;
  cancellation is guaranteed for pending application operations.

