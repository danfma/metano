// semantic-release configuration.
//
// Replaces the former `.releaserc.json`. It must stay a JS module because the
// release notes need a `writerOpts.transform` hook: conventional-commits-parser
// (6.x) treats every line after `BREAKING CHANGE:` as the note body, so git
// trailers (Co-Authored-By, Claude-Session, ...) leak into the changelog's
// breaking-changes section. The transform below trims those trailing trailer
// lines from each note before the preset renders them.
//
// Every other setting is identical to the JSON file it replaces.
import createConventionalcommitsPreset from "conventional-changelog-conventionalcommits";

// Git-style trailer tokens we emit or recognize. A trailer is a trailing
// `Token: value` line in the commit message's final paragraph.
const trailerLinePattern =
  /^(Co-Authored-By|Claude-Session|Claude-Model|Signed-off-by|Reviewed-by|Acked-by|Tested-by|Reported-by|Suggested-by|Helped-by|Requested-by|Originally-by|Based-on-patch|Cc|Link|See|Refs|Fixes):\s/i;

// Returns the commit with trailing trailer lines removed from each note's text.
function stripNoteTrailers(commit) {
  if (!commit.notes?.length) {
    return commit;
  }
  const notes = commit.notes.map((note) => {
    const lines = note.text.split("\n");
    let end = lines.length;
    while (end > 0 && (lines[end - 1].trim() === "" || trailerLinePattern.test(lines[end - 1]))) {
      end--;
    }
    return end === lines.length ? note : { ...note, text: lines.slice(0, end).join("\n") };
  });
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

const preset = await createConventionalcommitsPreset(presetConfig);

const parserOpts = {
  ...preset.parser,
  noteKeywords: ["BREAKING CHANGE", "BREAKING CHANGES", "BREAKING"],
};

const writerOpts = {
  ...preset.writer,
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
