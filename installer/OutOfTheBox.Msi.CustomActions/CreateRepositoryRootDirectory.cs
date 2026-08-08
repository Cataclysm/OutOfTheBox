// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.IO;
using WixToolset.Dtf.WindowsInstaller;

namespace OutOfTheBox.Msi.CustomActions
{
    /// <summary>
    /// Ensures the operator-supplied repository root directory (<c>REPOROOTDIR</c>, the
    /// <c>ConfigDlg</c> field bound to <c>OutOfTheBox__RootDirectory</c>) exists before the service
    /// starts, so a fresh path typed into the config dialog doesn't leave the service pointed at a
    /// directory that was never created - a real gap, since nothing else in the install ever
    /// touches this path.
    /// </summary>
    public static class CreateRepositoryRootDirectoryAction
    {
        /// <summary>
        /// Creates <paramref name="path"/> if it doesn't already exist. A no-op - not a
        /// clear-and-recreate - when it does, per <see cref="Directory.CreateDirectory(string)"/>'s
        /// own semantics: an existing repository root's contents (the operator's actual repos) must
        /// never be touched.
        /// </summary>
        public static void EnsureExists(string path) => Directory.CreateDirectory(path);

        /// <summary>
        /// MSI custom action entry point - scheduled in <c>InstallExecuteSequence</c> only (not
        /// <c>InstallUISequence</c>, unlike <c>ResolveSecrets</c>): <c>REPOROOTDIR</c> only holds
        /// its final, operator-confirmed value once <c>ConfigDlg</c> has been through and the
        /// installer has moved on to actually installing - running this any earlier would create
        /// the property's still-default value (<c>C:\repos</c>) before the operator ever got a
        /// chance to type a different path.
        /// </summary>
        [CustomAction]
        public static ActionResult CreateRepositoryRootDirectory(Session session)
        {
            EnsureExists(session["REPOROOTDIR"]);
            return ActionResult.Success;
        }
    }
}
