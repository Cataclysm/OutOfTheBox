// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Presentation.Dashboard;

/// <summary>A just-accepted clone's run id and the exact parameters that started it - enough for the caller to retry the identical clone if it turns out to need a credential.</summary>
public sealed record ClonedInfo(Guid RunId, string Url, string Name, string? Branch);
