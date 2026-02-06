using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    internal class PackageInstaller
    {
        private readonly GitHubContentsClient _client;

        public PackageInstaller(GitHubContentsClient client)
        {
            _client = client;
        }

        public IEnumerator InstallPackage(RepositoryInfo repository, PackageEntry package, string reference, string installRoot,
            string token, Action<InstallProgress> onProgress, Action<string> onError)
        {
            if (repository == null)
            {
                onError?.Invoke("Repository information is missing.");
                yield break;
            }

            if (package == null)
            {
                onError?.Invoke("Package information is missing.");
                yield break;
            }

            if (string.IsNullOrEmpty(reference))
            {
                onError?.Invoke("Package reference is missing.");
                yield break;
            }

            var errors = new List<string>();
            var files = new List<GitHubContentItem>();

            yield return CollectFiles(repository.owner, repository.name, package.pathInRepo, reference, token, files, errors);

            if (errors.Count > 0)
            {
                onError?.Invoke(errors[0]);
                yield break;
            }

            if (files.Count == 0)
            {
                onError?.Invoke("No files found for " + package.id + ".");
                yield break;
            }

            var packageRoot = Path.Combine(installRoot, package.id);
            if (Directory.Exists(packageRoot))
            {
                Directory.Delete(packageRoot, true);
            }

            Directory.CreateDirectory(packageRoot);

            var total = files.Count;
            var index = 0;

            foreach (var file in files)
            {
                index++;
                var relative = file.path.Substring(package.pathInRepo.Length).TrimStart('/', '\\');
                var localPath = Path.Combine(packageRoot, relative);

                onProgress?.Invoke(new InstallProgress
                {
                    title = "Installing " + package.id,
                    info = "Downloading " + file.path,
                    progress = total > 0 ? (float)index / total : 0f
                });

                GitHubRequestError downloadError = null;
                yield return _client.DownloadFile(file.download_url, localPath, token, null, err => downloadError = err);

                if (downloadError != null)
                {
                    onError?.Invoke(FormatError(downloadError));
                    yield break;
                }
            }

            onProgress?.Invoke(new InstallProgress
            {
                title = "Installing " + package.id,
                info = "Refreshing AssetDatabase",
                progress = 1f
            });

            AssetDatabase.Refresh();

            try
            {
                Client.Resolve();
            }
            catch (Exception)
            {
                // Resolve can fail when called during compilation; ignore.
            }
        }

        public void UninstallPackage(string installRoot, PackageEntry package)
        {
            if (package == null)
            {
                return;
            }

            var packageRoot = Path.Combine(installRoot, package.id);
            if (Directory.Exists(packageRoot))
            {
                Directory.Delete(packageRoot, true);
            }

            AssetDatabase.Refresh();
        }

        private IEnumerator CollectFiles(string owner, string repo, string basePath, string reference, string token,
            List<GitHubContentItem> files, List<string> errors)
        {
            GitHubContentItem[] items = null;
            GitHubRequestError requestError = null;

            yield return _client.GetContents(owner, repo, basePath, reference, token, result => items = result,
                err => requestError = err);

            if (requestError != null)
            {
                errors.Add(FormatError(requestError));
                yield break;
            }

            if (items == null)
            {
                yield break;
            }

            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                if (string.Equals(item.type, "file", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(item.download_url))
                    {
                        files.Add(item);
                    }
                }
                else if (string.Equals(item.type, "dir", StringComparison.OrdinalIgnoreCase))
                {
                    yield return CollectFiles(owner, repo, item.path, reference, token, files, errors);

                    if (errors.Count > 0)
                    {
                        yield break;
                    }
                }
            }
        }

        private static string FormatError(GitHubRequestError error)
        {
            if (error == null)
            {
                return "Unknown GitHub error.";
            }

            var message = error.ToString();
            if (error.statusCode == 403 && !string.IsNullOrEmpty(error.message) &&
                error.message.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                message += " (GitHub rate limit exceeded. Add a token or wait before retrying.)";
            }

            return message;
        }
    }

    internal struct InstallProgress
    {
        public string title;
        public string info;
        public float progress;
    }
}
