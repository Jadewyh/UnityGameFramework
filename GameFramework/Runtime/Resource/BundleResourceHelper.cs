﻿//-----------------------------------------------------------------------
// <copyright>
//     Copyright (c) 2018 Zhang Yang. All rights reserved.
// </copyright>
// <describe> #AssetBundle资源管理类# </describe>
// <email> yeozhang@qq.com </email>
// <time> #2018年6月24日 17点00分# </time>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;


namespace Wanderer.GameFramework
{
    public sealed class BundleResourceHelper : IResourceHelper
    {
        //路径类型
        private PathType _pathType;
        //路径
        private string _readPath;
        //资源引用
        private AssetBundleManifest _mainfest;

        private bool _isEncrypt;
        //解密密钥
      //  private EnciphererKey _enciphererkeyAsset;
        //所有资源AssetBundle引用
        private readonly Dictionary<string, AssetBundle> _allAssets = new Dictionary<string, AssetBundle>();
        //所有AssetBundle包含的资源
        private readonly Dictionary<string, KeyValuePair<AssetBundle, string[]>> _allAssetBundles =
            new Dictionary<string, KeyValuePair<AssetBundle, string[]>>();
        //资源路径映射ab包的名称
        private readonly Dictionary<string,string> _assetsPathMapAssetbundleName=new Dictionary<string, string>();

        /// <summary>
        /// 设置资源的路径,默认是为只读路径:Application.streamingAssetsPath;
        /// </summary>
        /// <param name="path"></param>
        public void SetResourcePath(PathType pathType, string rootAssetBundle = "AssetBundles/AssetBundles", bool isEncrypt = true)
        {
            switch (pathType)
            {
                case PathType.ReadOnly:
                    _readPath = Application.streamingAssetsPath;
                    #if UNITY_IOS && !UNITY_EDITOR
                    _readPath = $"file:///{_readPath}";
                    #endif
                    break;
                case PathType.ReadWrite:
                    _readPath = Application.persistentDataPath;
                    break;
                case PathType.DataPath:
                    _readPath = Application.dataPath;
                    break;
                case PathType.TemporaryCache:
                    _readPath = Application.temporaryCachePath;
                    break;
                default:
                    _readPath = Application.persistentDataPath;
                    break;
            }

            _pathType=pathType;


            string rootAbPath = Path.Combine(_readPath, rootAssetBundle);
            _readPath = Path.GetDirectoryName(rootAbPath);

            _isEncrypt=isEncrypt;

            //加载主包
            LoadPlatformMainfest(rootAbPath);
            //加载所有的资源路径与ab包名称的映射
            LoadAllAssetPathForAssetbundle(Path.Combine(_readPath,"assets"));
        }

        /// <summary>
        /// 加载ab包
        /// </summary>
        /// <param name="assetBundleName"></param>
        /// <returns></returns>
        public async Task<AssetBundle> LoadAssetBundle(string assetBundleName)
        {
            //加载Assetbundle
            AssetBundle assetBundle;
            KeyValuePair<AssetBundle, string[]> assetBundles;
            if (!_allAssetBundles.ContainsKey(assetBundleName))
            {
                string assetBundlePath = Path.Combine(_readPath, assetBundleName);
                // if (!File.Exists(assetBundlePath))
                //     throw new GameException("AssetBundle is Null");
                //加载assetbundle
                assetBundle = await LoadAssetBundleFromPath(assetBundlePath);
                //存储资源名称
                string[] assetNames = assetBundle.GetAllAssetNames();
                if (assetBundle.isStreamedSceneAssetBundle)
                    assetNames = assetBundle.GetAllScenePaths();
                foreach (var name in assetNames)
                {
                    if (!_allAssets.ContainsKey(name))
                        _allAssets.Add(name, assetBundle);
                }
                //存储assetbundle
                assetBundles = new KeyValuePair<AssetBundle, string[]>(assetBundle, assetNames);
                _allAssetBundles[assetBundleName] = assetBundles;
            }
            return assetBundles.Key;
        }

        /// <summary>
        /// 加载资源 同步加载
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="assetBundleName"></param>
        /// <param name="assetName"></param>
        /// <returns></returns>
        public  T LoadAssetSync<T>(string assetBundleName, string assetName) where T : Object
        {
            AssetBundle assetBundle;
            if (_allAssets.TryGetValue(assetName, out assetBundle))
            {
                 return assetBundle.LoadAsset<T>(assetName);
            }
            return null;
        }

        /// <summary>
        /// 加载资源
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="assetBundleName"></param>
        /// <param name="assetName"></param>
        /// <returns></returns>
        public async Task<T> LoadAsset<T>(string assetBundleName, string assetName) where T : Object
        {
            //转小写
            assetName = assetName.ToLower();

            //加载Assetbundle
            AssetBundle assetBundle;

            if (!_allAssets.TryGetValue(assetName, out assetBundle))
            {
                string assetBundlePath = Path.Combine(_readPath, assetBundleName);
                if (!File.Exists(assetBundlePath))
                    throw new GameException("AssetBundle is Null");
                //加载assetbundle
                assetBundle = await LoadAssetBundleFromPath(assetBundlePath);
                //存储资源名称
                string[] assetNames = assetBundle.GetAllAssetNames();
                if (assetBundle.isStreamedSceneAssetBundle)
                    assetNames = assetBundle.GetAllScenePaths();
                foreach (var name in assetNames)
                {
                    if (!_allAssets.ContainsKey(name))
                        _allAssets.Add(name, assetBundle);
                }
                //存储assetbundle
                _allAssetBundles[assetName] = new KeyValuePair<AssetBundle, string[]>(assetBundle, assetNames);
            }

            //加载依赖项
            await LoadDependenciesAssetBundel(assetBundleName);

            //加载资源
            var asset = await assetBundle.LoadAssetAsync(assetName);

            return (T)asset;
        }

         /// <summary>
		/// 加载资源 -- 同步加载
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="assetName"></param>
		/// <returns></returns>
		public async Task<T> LoadAssetSync<T>(string assetName) where T : UnityEngine.Object
        {
             assetName=assetName.ToLower();
            if(_assetsPathMapAssetbundleName.TryGetValue(assetName,out string abName))
            {
                if (!_allAssets.TryGetValue(assetName, out AssetBundle assetBundle))
                {
                     await LoadAssetBundle(abName);
                }
               return LoadAssetSync<T>(abName,assetName);
                
            }
            return null;
        }

		/// <summary>
		/// 加载资源
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="assetBundleName"></param>
		/// <param name="assetName"></param>
		///  <param name="unload"></param>
		/// <returns></returns>
		public Task<T> LoadAsset<T>(string assetName) where T : UnityEngine.Object
        {
            assetName=assetName.ToLower();
            if(_assetsPathMapAssetbundleName.TryGetValue(assetName,out string abName))
            {
                return LoadAsset<T>(abName,assetName);
            }
            return null;
        }

     

        /// <summary>
        /// 卸载掉资源
        /// </summary>
        /// <param name="assetBundleName"></param>
        /// <param name="unload"></param>
        public void UnloadAsset(string assetBundleName, bool unload = false)
        {
            KeyValuePair<AssetBundle, string[]> assetBundles;
            if (_allAssetBundles.TryGetValue(assetBundleName, out assetBundles))
            {
                if (!unload)
                    assetBundles.Key.Unload(false);
                else
                {
                    foreach (var item in assetBundles.Value)
                    {
                        if (_allAssets.ContainsKey(item))
                            _allAssets.Remove(item);
                    }
                    assetBundles.Key.Unload(true);
                    _allAssetBundles.Remove(assetBundleName);
                }

            }
        }

        /// <summary>
        /// 异步加载场景
        /// </summary>
        /// <param name="sceneName"></param>
        public async Task<AsyncOperation> LoadSceneAsync(string assetBundleName, string sceneName, LoadSceneMode mode = LoadSceneMode.Additive)
        {
            AsyncOperation asyncOperation = null;
            try
            {
                string assetBundlePath = Path.Combine(_readPath, assetBundleName);

                AssetBundle assetBundle = await LoadAssetBundleFromPath(assetBundlePath);
                //加载依赖项
                await LoadDependenciesAssetBundel(assetBundleName);

                asyncOperation = SceneManager.LoadSceneAsync(sceneName, mode);
                //场景加载完成卸载相关的引用
                asyncOperation.completed += (operation02) =>
                {
                    assetBundle.Unload(false);
                };

            }
            catch (GameException ex)
            {
                Debug.LogError(ex.ToString());
            }

            return asyncOperation;
        }

        /// <summary>
        /// 卸载场景
        /// </summary>
        /// <param name="sceneName"></param>
        public AsyncOperation UnloadSceneAsync(string sceneName)
        {
            return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(sceneName);
        }


        #region 事件回调
        /// <summary>
        /// 清理资源
        /// </summary>
        public void Clear()
        {
            foreach (var item in _allAssets.Values)
                if (item != null)
                    item.Unload(true);
            _allAssets.Clear();
            _allAssetBundles.Clear();
            _mainfest = null;
        }

        #endregion

        #region 内部函数
        /// <summary>
        /// 加载mainfest -- LoadFromFile
        /// </summary>
        private async void LoadPlatformMainfest(string rootBundlePath)
        {
            try
            {
                AssetBundle mainfestAssetBundle = await LoadAssetBundleFromPath(rootBundlePath);
                _mainfest = mainfestAssetBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                mainfestAssetBundle.Unload(false);
            }
            catch (GameException ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 加载资源路径映射的ab包的名称
        /// </summary>
        /// <param name="assetsPath"></param>
        /// <returns></returns>
        private async void LoadAllAssetPathForAssetbundle(string assetsPath)
        {
            using(UnityWebRequest request = new UnityWebRequest(assetsPath))
            {
                request.downloadHandler=new DownloadHandlerBuffer();
                await request.SendWebRequest();
             
                if(request.isNetworkError)
                {
                    throw new GameException($"Can't read assets path file from streamingasset: {assetsPath} error: {request.error}");
                }
                byte[] buffer = request.downloadHandler.data;
                using(MemoryStream stream =new EncryptMemoryStream(buffer))
                {
                    stream.Read(buffer,0,buffer.Length);
                    string content = System.Text.Encoding.UTF8.GetString(buffer);
                    _assetsPathMapAssetbundleName.Clear();
                    string[] lines=content.Split('\n');
                    foreach (var item in lines)
                    {
                        if(!string.IsNullOrEmpty(item))
                        {
                            string[] args = item.Split('\t');
                            if(args!=null&&args.Length>=2)
                            {
                                _assetsPathMapAssetbundleName[args[0].Trim()]=args[1].Trim();
                            }
                        }
                    }
                }

                //路径准备好了
                GameFrameworkMode.GetModule<EventManager>().Trigger<ResourceAssetPathsMapReadyEventArgs>(this);
            }
        }

        //同步加载AssetBundle
        private Task<AssetBundle> LoadAssetBundleFromPath(string path)
        {
            var taskResult = new TaskCompletionSource<AssetBundle>();

            if(_pathType==PathType.ReadOnly)
            {
               LoadAssetBundleFromStreamingAssets(path,(ab)=>{
                   taskResult.SetResult(ab);
               });
            }
            else
            {
                if (!File.Exists(path))
                    throw new GameException("Assetbundle not found :" + path);
                AssetBundle assetbundle;
                if(_isEncrypt)
                {
                    using (var stream = new EncryptFileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1024 * 4, false))
                    {
                        assetbundle = AssetBundle.LoadFromStream(stream);
                    }
                }
                else
                {
                    assetbundle = AssetBundle.LoadFromFile(path);
                }
                taskResult.SetResult(assetbundle);
            }   
           
            return taskResult.Task;
        }

        //加载引用的assetbundle --引用的assetbundle不卸载
        private async Task LoadDependenciesAssetBundel(string assetBundleName)
        {
            //加载相关依赖 依赖暂时不异步加载了
            string[] dependencies = _mainfest.GetAllDependencies(assetBundleName);
            foreach (var item in dependencies)
            {
                if (_allAssetBundles.ContainsKey(item))
                    continue;

                string dependenciesBundlePath = Path.Combine(_readPath, item);
                AssetBundle assetBundle = await LoadAssetBundleFromPath(dependenciesBundlePath);

                //存储资源名称
                string[] assetNames = assetBundle.GetAllAssetNames();
                if (assetBundle.isStreamedSceneAssetBundle)
                    assetNames = assetBundle.GetAllScenePaths();
                foreach (var name in assetNames)
                {
                    if (!_allAssets.ContainsKey(name))
                        _allAssets.Add(name, assetBundle);
                }
                //存储assetbundle
                _allAssetBundles[item] = new KeyValuePair<AssetBundle, string[]>(assetBundle, assetNames);
            }
        }


        /// <summary>
        /// 从StreamingAsset下面读取文件
        /// </summary>
        /// <param name="path"></param>
        /// <param name="callback"></param>
        /// <returns></returns>
        private async void LoadAssetBundleFromStreamingAssets(string path,Action<AssetBundle> callback)
        {   
            if(_isEncrypt)
            {
                using(UnityWebRequest request = new UnityWebRequest(path))
                {
                    request.downloadHandler=new DownloadHandlerBuffer();
                    await request.SendWebRequest();
                    if(request.isNetworkError)
                    {
                        throw new GameException($"Can't read assetbundle file from streamingasset: {path} error: {request.error}");
                    }
                    AssetBundle ab;
                    byte[] buffer=request.downloadHandler.data;
                    using(MemoryStream stream =new EncryptMemoryStream(buffer))
                    {
                        ab = AssetBundle.LoadFromStream(stream);
                        callback?.Invoke(ab);
                    }
                }
            }
            else
            {
                using(UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(path))
                {
                  //  request.downloadHandler=new DownloadHandlerAssetBundle();
                    await request.SendWebRequest();
                    if(request.isNetworkError)
                    {
                        throw new GameException($"Can't read assetbundle file from streamingasset: {path} error: {request.error}");
                    }
                    AssetBundle ab = DownloadHandlerAssetBundle.GetContent(request);
                    callback?.Invoke(ab);
                }
            }
            
        }

     

   


        #endregion

    }
}
