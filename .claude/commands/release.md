# Release Workflow

Additional hints from user (may be empty): $ARGUMENTS

Follow these steps exactly — do NOT skip ahead:

1. Run `git status` to check for uncommitted changes.
   - If there are modified/staged files: inform the user and **STOP**. Ask them to commit first (e.g. via `/commit`) before releasing.
   - Untracked files are fine — they are not released.

2. Run `nbgv get-version` and derive the tag name from `AssemblyInformationalVersion`:
   strip everything from `+` onward, e.g. `v1.0.110-beta`.
   Tell the user: "Tagging HEAD as <tag> and pushing…"

3. Run `nbgv tag` to create the annotated tag on HEAD.

4. Run `git push --follow-tags` to push the current branch and the new tag in one operation.
   The Nuke CI pipeline triggers on `v*` tags and handles the full release build and changelog.

5. Output: "Released <tag>."
