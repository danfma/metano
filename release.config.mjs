// semantic-release configuration.
//
// Replaces the former `.releaserc.json`. It must stay a JS module because the
// release notes need a `writerOpts.transform` hook: conventional-commits-parser
// (6.x) treats every line after `BREAKING CHANGE:` as the note body, so git
// trailers (Co-Authored-By, Claude-Session, ...) leak into the changelog's
// breaking-changes section. The transform below trims those trailing trailer
// lines from each note before the preset renders them.
//
// One intended change from the JSON file it replaces: both the commit analyzer
// and the release notes now use the same breaking-change keywords, including
// `BREAKING-CHANGE`, the Conventional Commits synonym. Before, only the
// analyzer had a keyword list and the notes used the preset default, so the two
// could disagree. `BREAKING-CHANGE:` footers now also trigger a major
// release. The bare `BREAKING` keyword is dropped: it is not a Conventional
// Commits keyword and the case-insensitive parser matched prose such as
// "Breaking out of the loop". Every other setting is identical.
import createConventionalcommitsPreset from "conventional-changelog-conventionalcommits";

// The trailers this repository's commits carry. Only these are stripped, so a
// note that ends with prose such as `See: docs/migration.md` keeps it.
const knownTrailerLinePattern = /^(Co-Authored-By|Claude-Session|Signed-off-by):\s/i;

const isBlankOrKnownTrailer = (line) => line.trim() === "" || knownTrailerLinePattern.test(line);

// Removes the trailing run of blank and known-trailer lines; trailers in the
// middle of a note are kept.
function stripTrailingTrailers(text) {
  const lines = text.split("\n");
  const lastContentIndex = lines.findLastIndex((line) => !isBlankOrKnownTrailer(line));

  return lines.slice(0, lastContentIndex + 1).join("\n");
}

function stripNoteTrailers(commit) {
  if (!commit.notes?.length) {
    return commit;
  }

  const notes = commit.notes.map((note) => ({ ...note, text: stripTrailingTrailers(note.text) }));

  return { ...commit, notes };
}

const presetConfig = {
  types: [
    { type: "feat", section: "✨ Features" },
    { type: "fix", section: "🐛 Bug Fixes" },
    { type: "perf", section: "⚡ Performance" },
    { type: "refactor", section: "♻️ Refactor" },
    { type: "docs", section: "📝 Documentation", hidden: false },
    { type: "test", section: "🧪 Tests", hidden: true },
    { type: "build", section: "🔧 Build", hidden: true },
    { type: "ci", section: "🔧 CI", hidden: true },
    { type: "chore", section: "🧰 Maintenance", hidden: true },
    { type: "style", section: "💄 Style", hidden: true },
  ],
};

// Created here only to wrap the preset's own writer transform; both plugins
// still load the preset themselves and merge the options below over it.
const preset = await createConventionalcommitsPreset(presetConfig);

const parserOpts = {
  noteKeywords: ["BREAKING CHANGE", "BREAKING-CHANGE", "BREAKING CHANGES"],
};

const writerOpts = {
  headerPartial: "## {{version}}\n\n{{#if date}}_{{date}}_\n\n{{/if}}",
  transform: (commit, context) => preset.writer.transform(stripNoteTrailers(commit), context),
};

export default {
  branches: ["main"],
  tagFormat: "v${version}",
  plugins: [
    [
      "@semantic-release/commit-analyzer",
      {
        preset: "conventionalcommits",
        releaseRules: [
          { type: "feat", release: "minor" },
          { type: "fix", release: "patch" },
          { type: "perf", release: "patch" },
          { type: "refactor", release: "patch" },
          { type: "docs", release: false },
          { type: "chore", release: false },
          { type: "test", release: false },
          { type: "style", release: false },
          { type: "ci", release: false },
          { type: "build", release: false },
          { scope: "no-release", release: false },
          { breaking: true, release: "major" },
        ],
        parserOpts,
      },
    ],
    [
      "@semantic-release/release-notes-generator",
      {
        preset: "conventionalcommits",
        presetConfig,
        parserOpts,
        writerOpts,
      },
    ],
    [
      "@semantic-release/changelog",
      {
        changelogFile: "CHANGELOG.md",
        changelogTitle:
          "# Changelog\n\nAll notable changes to Metano are documented here. The format follows\n[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project\nadheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).",
      },
    ],
    [
      "@semantic-release/exec",
      {
        publishCmd:
          "dotnet pack Metano-packages.slnx -c Release -p:Version=${nextRelease.version} -p:MinVerVersionOverride=${nextRelease.version} -p:PackageVersion=${nextRelease.version} -o ./artifacts && dotnet nuget push './artifacts/*.nupkg' --api-key \"$NUGET_API_KEY\" --source https://api.nuget.org/v3/index.json --skip-duplicate",
      },
    ],
    [
      "@semantic-release/git",
      {
        assets: ["CHANGELOG.md"],
        message: "chore(release): ${nextRelease.version} [skip ci]\n\n${nextRelease.notes}",
      },
    ],
    [
      "@semantic-release/github",
      {
        successComment: false,
        failComment: false,
      },
    ],
  ],
};
