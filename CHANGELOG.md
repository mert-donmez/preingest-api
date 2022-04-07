# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

- Nothing yet

## [1.1.0] - 2021-07-12

### Added

- Support for "pre-wash" custom XSLT transformations that run before validations and standard
  transformations

### Changed

- Force default security tag to `closed` (both in `xip2topx.xslt` and the SIP Creator command line)
- Define mapping of security tags to use `Tag_<owner>_Publiek`, `Tag_<owner>_Intern_some_code` and
  all (Preservica security tags are case-sensitive)
- Add option to force the security tag to `public` (next to `open` and `closed`), regardless what is
  given in ToPX (only `open` and `closed` are defaults for every Preservica installation, but most
  will also have `public` after reading the manual for Universal Access)
- Remove validation and fix for long names (should not be required since Preservica 6.3.0)
- Remove fix for file size (assume a bare number without any unit such as "bytes" or"Kb"; a fix can
  still be applied [using a pre-wash transformation](prewash/fix_omvang.xslt), although that changes
  the ToPX source so should be used with care)

### Fixed

- Allow changing location of database (the SQLite database should not be stored on a network share)

### New settings

- `XSLWEBPREWASHFOLDER` to define the location of the custom XML stylesheets
- `DBFOLDER` to define the location of the SQLite database file (this MUST be on a local disk)


## 1.0.0 - 2021-04-01

Initial release, supporting validation and transformation from ToPX to Preservica XIPv4.

[Unreleased]: https://github.com/noord-hollandsarchief/preingest/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/noord-hollandsarchief/preingest/compare/v1.0.0...v1.1.0
