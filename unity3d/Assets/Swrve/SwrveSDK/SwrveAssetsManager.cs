using UnityEngine;
using UnityEngine.Networking;

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SwrveUnity.Helpers;

namespace SwrveUnity
{
public class SwrveAssetsManager : ISwrveAssetsManager
{
    private MonoBehaviour Container;
    private string SwrveTemporaryPath;

    public SwrveAssetsManager(MonoBehaviour container, string swrveTemporaryPath)
    {
        Container = container;
        SwrveTemporaryPath = swrveTemporaryPath;
        AssetsOnDisk = new HashSet<string>();
        MissingAssetsQueue = new HashSet<SwrveAssetsQueueItem>();
    }

    public string CdnImages
    {
        get;
        set;
    }

    public string CdnFonts
    {
        get;
        set;
    }

    public HashSet<string> AssetsOnDisk
    {
        get;
        set;
    }

    public HashSet<SwrveAssetsQueueItem> MissingAssetsQueue
    {
        get;
        set;
    }

    public IEnumerator DownloadAnyMissingAssets(Action callBack)
    {
        if (MissingAssetsQueue.Count > 0) {
            // Make a copy of the current MissingAssetsQueue so it can processed in the coroutine safely
            HashSet<SwrveAssetsQueueItem> currentMissingQueue = new HashSet<SwrveAssetsQueueItem>();
            currentMissingQueue.UnionWith(MissingAssetsQueue);
            MissingAssetsQueue.Clear();
            SwrveLog.Log("There were " + currentMissingQueue.Count + " Assets not yet downloaded. Retrieving them now...");
            yield return StartTask("SwrveAssetsManager.DownloadAssetQueue", DownloadAssetQueue(currentMissingQueue));

            if (callBack != null) {
                callBack.Invoke();
            }
        }

        TaskFinished("SwrveAssetsManager.DownloadMissingAssets");
    }

    public IEnumerator DownloadAssets(HashSet<SwrveAssetsQueueItem> autoShowQueue, HashSet<SwrveAssetsQueueItem> assetQueue, Action callBack)
    {
        yield return StartTask("SwrveAssetsManager.DownloadAssetQueue", DownloadAssetQueue(autoShowQueue));

        if (callBack != null) {
            callBack.Invoke(); // AutoShowMessages;
        }

        yield return StartTask("SwrveAssetsManager.DownloadAssetQueue", DownloadAssetQueue(assetQueue));

        TaskFinished("SwrveAssetsManager.DownloadAssets");
    }

    public IEnumerator DownloadAssets(HashSet<SwrveAssetsQueueItem> assetsQueue, Action callBack)
    {
        yield return StartTask("SwrveAssetsManager.DownloadAssetQueue", DownloadAssetQueue(assetsQueue));

        if (callBack != null) {
            callBack.Invoke(); // AutoShowMessages;
        }
        TaskFinished("SwrveAssetsManager.DownloadAssets");
    }

    public IEnumerator DownloadAssets(HashSet<SwrveAssetsQueueItem> assetsQueue, Action<object> callBack, object arg)
    {
        yield return StartTask("SwrveAssetsManager.DownloadAssetQueue", DownloadAssetQueue(assetsQueue));

        if (callBack != null) {
            callBack(arg);
        }
        TaskFinished("SwrveAssetsManager.DownloadAssets");
    }

    private IEnumerator DownloadAssetQueue(HashSet<SwrveAssetsQueueItem> assetsQueue)
    {
        IEnumerator<SwrveAssetsQueueItem> enumerator = assetsQueue.GetEnumerator();
        while (enumerator.MoveNext()) {
            SwrveAssetsQueueItem item = enumerator.Current;
            if (!CheckAsset(item.Name)) {
                if (item.IsExternalSource) {
                    yield return StartTask("SwrveAssetsManager.DownloadExternalAsset", DownloadExternalAsset(item));
                } else {
                    yield return StartTask("SwrveAssetsManager.DownloadAsset", DownloadAsset(item));
                }
            } else {
                AssetsOnDisk.Add(item.Name); // Already downloaded
            }
        }

        TaskFinished("SwrveAssetsManager.DownloadAssetQueue");
    }

    protected virtual IEnumerator DownloadAsset(SwrveAssetsQueueItem item)
    {
        string cdn = item.IsImage ? CdnImages : CdnFonts;
        string url = cdn + item.Name;
        SwrveLog.Log("Downloading asset: " + url);
        UnityWebRequest www = (item.IsImage) ? UnityWebRequestTexture.GetTexture(url) : new UnityWebRequest(url);
        if (!item.IsImage) {
            DownloadHandlerBuffer dH = new DownloadHandlerBuffer();
            www.downloadHandler = dH;
        }
        yield return www.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
        if (www.result == UnityWebRequest.Result.Success) {
#else
        if (!www.isNetworkError && !www.isHttpError) {
#endif
            if (item.IsImage) {
                SaveImageAsset(item, www);
            } else {
                SaveBinaryAsset(item, www);
            }
        } else {
            MissingAssetsQueue.Add(item);
        }
        TaskFinished("SwrveAssetsManager.DownloadAsset");
    }

    protected virtual IEnumerator DownloadExternalAsset(SwrveAssetsQueueItem item)
    {
        string url = item.Digest;
        SwrveLog.Log("Downloading external asset: " + url);
        UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
        yield return www.SendWebRequest();

#if UNITY_2020_OR_HIGHER
        if (www.result == UnityWebRequest.Result.Success) {
#else
        if (!www.isNetworkError && !www.isHttpError) {
#endif
            if (item.IsImage) {
                SaveImageAsset(item, www);
            }
        } else {
            SwrveLog.Log("Asset could not be downloaded: " + url);
            SwrveQaUser.AssetFailedToDownload(item.Name, item.Digest, "Asset could not be downloaded");
        }
        TaskFinished("SwrveAssetsManager.DownloadExternalAsset");
    }

    private bool CheckAsset(string fileName)
    {
        if (CrossPlatformFile.Exists(GetTemporaryPathFileName(fileName))) {
            return true;
        }
        return false;
    }

    private string GetTemporaryPathFileName(string fileName)
    {
        return Path.Combine(SwrveTemporaryPath, fileName);
    }

    protected virtual void SaveImageAsset(SwrveAssetsQueueItem item, UnityWebRequest www)
    {
        Texture2D loadedTexture = ((DownloadHandlerTexture)www.downloadHandler).texture;
        if (loadedTexture != null) {
            byte[] rawBytes = www.downloadHandler.data;
            if (rawBytes == null) {
                SwrveLog.LogError("SwrveAssetsManager cannot download asset because downloadHandler.data is null. Please check unity version and bug https://issuetracker.unity3d.com/issues/unitywebrequest-downloadhandler-dot-data-is-null-after-downloading-a-texture-using-unitywebrequesttexture-dot-gettexture");
                return;
            }
            string sha1 = SwrveHelper.sha1(rawBytes);
            if (sha1 == item.Digest || item.IsExternalSource) {
                byte[] bytes = loadedTexture.EncodeToPNG();
                string filePath = GetTemporaryPathFileName(item.Name);
                SwrveLog.Log("Saving to " + filePath);
                CrossPlatformFile.SaveBytes(filePath, bytes);
                bytes = null;
                Texture2D.Destroy(loadedTexture);
                AssetsOnDisk.Add(item.Name);
            } else {
                string reason = (sha1 == item.Digest) ? "" : "Did not match digest:" + sha1 + "";
                SwrveLog.Log("Error downloading image assetItem: " + item.Name + ". " + reason);
            }
        }
    }

    protected virtual void SaveBinaryAsset(SwrveAssetsQueueItem item, UnityWebRequest www)
    {
        byte[] bytes = www.downloadHandler.data;
        if (bytes == null) {
            SwrveLog.LogError("SwrveAssetsManager cannot download asset because downloadHandler.data is null. Please check unity version and bug https://issuetracker.unity3d.com/issues/unitywebrequest-downloadhandler-dot-data-is-null-after-downloading-a-texture-using-unitywebrequesttexture-dot-gettexture");
            return;
        }

        string sha1 = SwrveHelper.sha1(bytes);
        if (sha1 == item.Digest || item.IsExternalSource) {
            string filePath = GetTemporaryPathFileName(item.Name);
            SwrveLog.Log("Saving to " + filePath);
            CrossPlatformFile.SaveBytes(filePath, bytes);
            bytes = null;
            AssetsOnDisk.Add(item.Name);
        } else {
            string reason = (sha1 == item.Digest) ? "" : "Did not match digest:" + sha1 + "";
            SwrveLog.Log("Error downloading binary assetItem: " + item.Name + ". " + reason);
        }
    }

    public virtual Coroutine StartTask(string tag, IEnumerator task)
    {
        return Container.StartCoroutine(task);
    }

    protected virtual void TaskFinished(string tag)
    {
    }
}
}
