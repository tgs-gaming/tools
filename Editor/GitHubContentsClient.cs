using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace com.tgs.packagemanager.editor
{
    internal class GitHubContentsClient
    {
        private const string ApiBase = "https://api.github.com/repos";
        private readonly string _userAgent;

        public GitHubContentsClient(string userAgent)
        {
            _userAgent = string.IsNullOrEmpty(userAgent) ? "CompanyToolsPackageManager" : userAgent;
        }

        public IEnumerator GetContents(string owner, string repo, string path, string reference, string token,
            Action<GitHubContentItem[]> onSuccess, Action<GitHubRequestError> onError)
        {
            var url = ApiBase + "/" + owner + "/" + repo + "/contents/" + path + "?ref=" + reference;
            using (var request = CreateRequest(url, token, true))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(CreateError(request));
                    yield break;
                }

                var body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                GitHubContentItem[] items;

                try
                {
                    items = JsonHelper.FromJsonArray<GitHubContentItem>(body);
                    if (items == null || items.Length == 0)
                        throw new Exception();
                }
                catch (Exception)
                {
                    var single = JsonUtility.FromJson<GitHubContentItem>(body);
                    items = single != null ? new[] { single } : new GitHubContentItem[0];
                }

                onSuccess?.Invoke(items ?? new GitHubContentItem[0]);
            }
        }

        public IEnumerator DownloadFile(string downloadUrl, string outputPath, string token, Action onSuccess,
            Action<GitHubRequestError> onError)
        {
            using (var request = CreateRequest(downloadUrl, token, false))
            {
                var directory = System.IO.Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                request.downloadHandler = new DownloadHandlerFile(outputPath);
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(CreateError(request));
                    yield break;
                }

                onSuccess?.Invoke();
            }
        }

        public IEnumerator GetTags(string owner, string repo, string token, Action<GitHubTag[]> onSuccess,
            Action<GitHubRequestError> onError)
        {
            var url = ApiBase + "/" + owner + "/" + repo + "/tags";
            using (var request = CreateRequest(url, token, true))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(CreateError(request));
                    yield break;
                }

                var body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                GitHubTag[] tags;

                try
                {
                    tags = JsonHelper.FromJsonArray<GitHubTag>(body);
                }
                catch (Exception)
                {
                    tags = new GitHubTag[0];
                }

                onSuccess?.Invoke(tags ?? new GitHubTag[0]);
            }
        }

        public IEnumerator GetBranches(string owner, string repo, string token, Action<GitHubBranch[]> onSuccess,
            Action<GitHubRequestError> onError)
        {
            var url = ApiBase + "/" + owner + "/" + repo + "/branches";
            using (var request = CreateRequest(url, token, true))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(CreateError(request));
                    yield break;
                }

                var body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                GitHubBranch[] branches;

                try
                {
                    branches = JsonHelper.FromJsonArray<GitHubBranch>(body);
                }
                catch (Exception)
                {
                    branches = new GitHubBranch[0];
                }

                onSuccess?.Invoke(branches ?? new GitHubBranch[0]);
            }
        }

        public IEnumerator DownloadText(string downloadUrl, string token, Action<string> onSuccess,
            Action<GitHubRequestError> onError)
        {
            var url = AddCacheBuster(downloadUrl);
            using (var request = CreateRequest(url, token, false))
            {
                request.SetRequestHeader("Cache-Control", "no-cache");
                request.SetRequestHeader("Pragma", "no-cache");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(CreateError(request));
                    yield break;
                }

                var text = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                onSuccess?.Invoke(text);
            }
        }

        private static string AddCacheBuster(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            var separator = url.Contains("?") ? "&" : "?";
            return url + separator + "t=" + DateTime.UtcNow.Ticks;
        }

        private UnityWebRequest CreateRequest(string url, string token, bool isApi)
        {
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("User-Agent", _userAgent);

            if (isApi)
            {
                request.SetRequestHeader("Accept", "application/vnd.github.v3+json");
            }

            if (!string.IsNullOrEmpty(token))
            {
                request.SetRequestHeader("Authorization", "token " + token);
            }

            return request;
        }

        private GitHubRequestError CreateError(UnityWebRequest request)
        {
            var error = new GitHubRequestError
            {
                statusCode = request.responseCode,
                message = request.error,
                rawBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty
            };

            if (!string.IsNullOrEmpty(error.rawBody))
            {
                try
                {
                    var apiError = JsonUtility.FromJson<GitHubErrorBody>(error.rawBody);
                    if (apiError != null && !string.IsNullOrEmpty(apiError.message))
                    {
                        error.message = apiError.message;
                    }
                }
                catch (Exception)
                {
                    // Ignore parse errors and keep raw message.
                }
            }

            return error;
        }
    }

    [Serializable]
    internal class GitHubContentItem
    {
        public string name;
        public string path;
        public string type;
        public string download_url;
        public string url;
    }

    [Serializable]
    internal class GitHubTag
    {
        public string name;
        public GitHubTagCommit commit;
    }

    [Serializable]
    internal class GitHubTagCommit
    {
        public string sha;
        public string url;
    }

    [Serializable]
    internal class GitHubBranch
    {
        public string name;
        public GitHubBranchCommit commit;
    }

    [Serializable]
    internal class GitHubBranchCommit
    {
        public string sha;
        public string url;
    }

    [Serializable]
    internal class GitHubErrorBody
    {
        public string message;
    }

    internal class GitHubRequestError
    {
        public long statusCode;
        public string message;
        public string rawBody;

        public override string ToString()
        {
            return "HTTP " + statusCode + ": " + message;
        }
    }

    internal static class JsonHelper
    {
        [Serializable]
        private class Wrapper<T>
        {
            public T[] items;
        }

        public static T[] FromJsonArray<T>(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new T[0];
            }

            var wrapped = "{\"items\":" + json + "}";
            var wrapper = JsonUtility.FromJson<Wrapper<T>>(wrapped);
            return wrapper != null && wrapper.items != null ? wrapper.items : new T[0];
        }
    }
}
