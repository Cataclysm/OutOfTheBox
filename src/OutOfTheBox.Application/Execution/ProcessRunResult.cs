// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Execution;

/// <summary>The result of a process that ran to completion (as opposed to being killed by a timeout/cancellation).</summary>
public sealed record ProcessRunResult(int ExitCode);
