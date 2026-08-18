# Icons

The SVGs in this directory are vendored from [Lucide](https://lucide.dev)
(https://github.com/lucide-icons/lucide), ISC-licensed - see `LICENSE.txt`.
Unmodified except that each file already ships with `stroke="currentColor"`,
which this dashboard relies on to recolor icons via CSS (including the
green/red completion flash for repository git actions) without editing the
SVGs themselves.

Vendored subset (dashboard action icons only, not the full icon set):

| File | Used for |
|---|---|
| `trash-2.svg` | Delete repository |
| `download.svg` | Clone repository |
| `circle-arrow-down.svg` | Pull |
| `circle-arrow-up.svg` | Push |
| `chevrons-up.svg` | Force push |
| `refresh-cw.svg` | Fetch |
| `eraser.svg` | Clean (`git clean -xdf`) |
| `git-branch.svg` | Branch-switch control |
| `list-filter.svg` | Clear filters |
| `x.svg` | Cancel / close dialog |
| `check.svg` | Confirm dialog |
| `loader-circle.svg` | In-flight clone (cancel-clone row state) |
| `folder.svg` | File tree: collapsed folder |
| `folder-open.svg` | File tree: expanded folder |
| `file.svg` | File tree: file |
| `pencil.svg` | File tree: rename |
| `tag.svg` | Commit graph: tag ref pill |
| `map-pin.svg` | Commit graph: checkout commit (as detached HEAD) |
| `arrow-up.svg` | Git status indicator: commits ahead of upstream |
| `arrow-down.svg` | Git status indicator: commits behind upstream |
| `triangle-alert.svg` | Git status indicator: upstream remote branch gone |
| `circle-user.svg` | Commit detail: author/committer avatar fallback (no Gravatar image for that email) |
| `key.svg` | Needs-credential marker next to a repository's name; Save button in the credential dialogs (Add/Edit, clone-retry PAT prompt) |
| `plus.svg` | Credentials page: add a new credential |
| `external-link.svg` | About page: link to the GitHub repository |

To add another icon, download the matching `<name>.svg` from
`https://raw.githubusercontent.com/lucide-icons/lucide/main/icons/<name>.svg`
and add a row above.
