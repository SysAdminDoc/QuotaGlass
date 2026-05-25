# Changelog

All notable changes to QuotaGlass will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial repo scaffold: solution layout, MIT license, .gitignore, README, ROADMAP.
- Research dossier in `docs/research.md` covering the OSS Windows widget landscape,
  native-messaging architecture, library picks, and the competitive matrix.
- Shared snapshot model targeting parity with the `AI-Usage_Tracker` browser
  extension bucket schema.
- Native messaging host (`QuotaGlass.NMH`) console executable with stdin/stdout
  4-byte length-prefix framing, snapshot persistence, and Windows registry
  install/uninstall flags.
- WPF widget (`QuotaGlass.Widget`) always-on-top borderless surface with
  draggable Catppuccin Mocha glass card, radial-ring countdowns, and
  file-watcher reload on snapshot changes.

## [0.1.0] — TBD

Initial release. See ROADMAP.md `Now — v0.1.0` for scope.
