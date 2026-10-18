using System;
using System.Collections.Generic;
using System.Linq;
using ProjectT.Addressable;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

internal static partial class FirebaseCacheTests
{
    private static void RunProviderScenarios()
    {
        Run("Hash 프로바이더는 받은 URL의 텍스트를 결과로 넘긴다", () =>
        {
            var request = Provide<FirebaseStorageHashProvider>("gs://bucket/catalog.hash", typeof(string));
            FirebaseBoundary.ResolvePending();
            Check(FirebaseBoundary.Provided.Count == 1, "받은 URL로 내부 로드를 시작한다");
            var inner = FirebaseBoundary.Provided[0];
            Check(inner.Location.InternalId == "https://storage.test/bucket/catalog.hash", "내부 로드가 받은 URL을 쓴다");
            Check(inner.Location.ProviderId == typeof(TextDataProvider).FullName && inner.Type == typeof(string), "텍스트 프로바이더로 문자열을 읽는다");

            inner.Complete("hash-value");
            Check(request.CompleteCount == 1 && request.Status && (string)request.Result == "hash-value", "내부 결과로 성공한다 (" + Describe(request) + ")");
            return Describe(request);
        });

        Run("Hash 프로바이더는 URL 요청 실패를 원래 예외로 넘긴다", () =>
        {
            var request = Provide<FirebaseStorageHashProvider>("gs://bucket/failed.hash", typeof(string));
            FirebaseBoundary.Fetches[0].Source.SetException(new InvalidOperationException("hash url failed"));
            FirebaseBoundary.RunMainThread();
            Check(request.CompleteCount == 1 && !request.Status && Carries(request, "hash url failed"), "URL 오류로 실패한다 (" + Describe(request) + ")");
            Check(FirebaseBoundary.Provided.Count == 0 && UnityEngine.Debug.Errors.Count == 0, "내부 로드와 오류 로그가 없다");
            return Describe(request);
        });

        Run("Hash·Json 프로바이더는 내부 로드 실패를 원래 예외로 넘긴다", () =>
        {
            var hash = Provide<FirebaseStorageHashProvider>("gs://bucket/inner.hash", typeof(string));
            var catalog = Provide<FirebaseStorageJsonAssetProvider>("gs://bucket/inner.json", typeof(ContentCatalogData));
            FirebaseBoundary.ResolvePending();
            Check(FirebaseBoundary.Provided.Count == 2, "받은 URL로 내부 로드를 시작한다");
            foreach (var inner in FirebaseBoundary.Provided)
                inner.Fail(new InvalidOperationException("inner load failed: " + inner.Location.InternalId));
            string trace = "hash=" + Describe(hash) + " catalog=" + Describe(catalog);

            Check(hash.CompleteCount == 1 && !hash.Status && Carries(hash, "inner.hash"), "Hash 요청이 내부 오류로 실패한다 (" + trace + ")");
            Check(catalog.CompleteCount == 1 && !catalog.Status && Carries(catalog, "inner.json"), "Json 요청이 내부 오류로 실패한다 (" + trace + ")");
            return trace;
        });

        Run("Hash·Json 프로바이더는 내부 로드가 끝나면 성공과 실패 모두 내부 핸들을 해제한다", () =>
        {
            Provide<FirebaseStorageHashProvider>("gs://bucket/released.hash", typeof(string));
            Provide<FirebaseStorageHashProvider>("gs://bucket/released-failed.hash", typeof(string));
            Provide<FirebaseStorageJsonAssetProvider>("gs://bucket/released.json", typeof(ContentCatalogData));
            Provide<FirebaseStorageJsonAssetProvider>("gs://bucket/released-failed.json", typeof(ContentCatalogData));
            FirebaseBoundary.ResolvePending();
            Check(FirebaseBoundary.Provided.Count == 4, "받은 URL로 내부 로드를 시작한다");
            Check(FirebaseBoundary.Provided.All(inner => !inner.Released()), "끝나기 전에는 해제하지 않는다");

            foreach (var inner in FirebaseBoundary.Provided)
            {
                if (inner.Location.InternalId.Contains("failed"))
                    inner.Fail(new InvalidOperationException("inner load failed"));
                else if (inner.Type == typeof(string))
                    inner.Complete("hash-value");
                else
                    inner.Complete(new ContentCatalogData());
            }
            string trace = string.Join(" ", FirebaseBoundary.Provided.Select(inner => inner.Location.InternalId + "=" + inner.Released()));

            Check(FirebaseBoundary.Provided.All(inner => inner.Released()), "모든 내부 핸들을 해제한다 (" + trace + ")");
            return trace;
        });

        Run("Hash·Json 프로바이더는 동시에 받은 요청을 각자의 결과로 완료한다", () =>
        {
            var firstHash = Provide<FirebaseStorageHashProvider>("gs://bucket/first.hash", typeof(string));
            var secondHash = Provide<FirebaseStorageHashProvider>("gs://bucket/second.hash", typeof(string));
            var firstCatalog = Provide<FirebaseStorageJsonAssetProvider>("gs://bucket/first.json", typeof(ContentCatalogData));
            var secondCatalog = Provide<FirebaseStorageJsonAssetProvider>("gs://bucket/second.json", typeof(ContentCatalogData));
            FirebaseBoundary.ResolvePending();
            var catalogs = new Dictionary<string, ContentCatalogData>();
            foreach (var inner in FirebaseBoundary.Provided)
            {
                if (inner.Type == typeof(string))
                {
                    inner.Complete(inner.Location.InternalId);
                }
                else
                {
                    catalogs[inner.Location.InternalId] = new ContentCatalogData();
                    inner.Complete(catalogs[inner.Location.InternalId]);
                }
            }
            string trace = "firstHash=" + Describe(firstHash) + " secondHash=" + Describe(secondHash) + " firstCatalog=" + Describe(firstCatalog) + " secondCatalog=" + Describe(secondCatalog);

            Check(firstHash.CompleteCount == 1 && (string)firstHash.Result == "https://storage.test/bucket/first.hash", "앞선 Hash 요청이 자기 결과로 완료된다 (" + trace + ")");
            Check(secondHash.CompleteCount == 1 && (string)secondHash.Result == "https://storage.test/bucket/second.hash", "뒤의 Hash 요청이 자기 결과로 완료된다 (" + trace + ")");
            Check(firstCatalog.CompleteCount == 1 && catalogs.TryGetValue("https://storage.test/bucket/first.json", out var first) && firstCatalog.Result == first, "앞선 Json 요청이 자기 결과로 완료된다 (" + trace + ")");
            Check(secondCatalog.CompleteCount == 1 && catalogs.TryGetValue("https://storage.test/bucket/second.json", out var second) && secondCatalog.Result == second, "뒤의 Json 요청이 자기 결과로 완료된다 (" + trace + ")");
            return trace;
        });

        Run("Json 프로바이더는 gs://가 아닌 주소를 기본 프로바이더로 넘긴다", () =>
        {
            var request = Provide<FirebaseStorageJsonAssetProvider>("local/catalog.json", typeof(ContentCatalogData));
            Check(request.BaseProvided && FirebaseBoundary.Fetches.Count == 0 && request.CompleteCount == 0, "URL 요청 없이 기본 프로바이더가 처리한다");
            return Describe(request);
        });

        Run("Json 프로바이더는 받은 URL의 카탈로그를 결과로 넘긴다", () =>
        {
            var request = Provide<FirebaseStorageJsonAssetProvider>("gs://bucket/catalog.json", typeof(ContentCatalogData));
            FirebaseBoundary.ResolvePending();
            Check(FirebaseBoundary.Provided.Count == 1, "받은 URL로 내부 로드를 시작한다");
            var inner = FirebaseBoundary.Provided[0];
            Check(inner.Location.InternalId == "https://storage.test/bucket/catalog.json", "내부 로드가 받은 URL을 쓴다");
            Check(inner.Location.ProviderId == typeof(JsonAssetProvider).FullName && inner.Type == typeof(ContentCatalogData), "Json 프로바이더로 카탈로그를 읽는다");

            var catalog = new ContentCatalogData();
            inner.Complete(catalog);
            Check(request.CompleteCount == 1 && request.Status && request.Result == catalog, "내부 결과로 성공한다 (" + Describe(request) + ")");
            return Describe(request);
        });

        Run("Json 프로바이더는 카탈로그가 아닌 타입을 NotSupportedException으로 실패시킨다", () =>
        {
            var request = Provide<FirebaseStorageJsonAssetProvider>("gs://bucket/other.json", typeof(string));
            FirebaseBoundary.ResolvePending();
            Check(request.CompleteCount == 1 && !request.Status && request.Exception is NotSupportedException, "지원하지 않는 타입으로 실패한다 (" + Describe(request) + ")");
            Check(FirebaseBoundary.Provided.Count == 0, "내부 로드를 시작하지 않는다");
            return Describe(request);
        });

        Run("Json 프로바이더는 URL 요청 실패를 원래 예외로 넘긴다", () =>
        {
            var request = Provide<FirebaseStorageJsonAssetProvider>("gs://bucket/failed.json", typeof(ContentCatalogData));
            FirebaseBoundary.Fetches[0].Source.SetException(new InvalidOperationException("json url failed"));
            FirebaseBoundary.RunMainThread();
            Check(request.CompleteCount == 1 && !request.Status && Carries(request, "json url failed"), "URL 오류로 실패한다 (" + Describe(request) + ")");
            return Describe(request);
        });

        Run("AssetBundle 프로바이더는 gs://가 아닌 주소를 기본 프로바이더로 넘긴다", () =>
        {
            var request = Provide<FirebaseStorageAssetBundleProvider>("local/a.bundle", typeof(IAssetBundleResource));
            Check(request.BaseProvided && FirebaseBoundary.Fetches.Count == 0 && request.CompleteCount == 0, "URL 요청 없이 기본 프로바이더가 처리한다");
            return Describe(request);
        });

        Run("AssetBundle 프로바이더는 받은 URL을 캐시하고 원래 요청을 기본 프로바이더가 그 URL로 읽게 한다", () =>
        {
            var request = Provide<FirebaseStorageAssetBundleProvider>("gs://bucket/bundle/main.bundle", typeof(IAssetBundleResource));
            Check(!request.BaseProvided, "URL을 받기 전에는 로드하지 않는다");
            FirebaseBoundary.ResolvePending();
            Check(Transform("gs://bucket/bundle/main.bundle") == "https://storage.test/bucket/bundle/main.bundle", "받은 URL로 주소를 바꾼다");
            Check(request.BaseProvided && request.BaseUrl == "https://storage.test/bucket/bundle/main.bundle", "기본 프로바이더가 받은 URL로 읽는다 (" + Describe(request) + " url=" + request.BaseUrl + ")");
            Check(FirebaseBoundary.Provided.Count == 0, "따로 내부 로드를 만들지 않는다");

            var again = Provide<FirebaseStorageAssetBundleProvider>("gs://bucket/bundle/main.bundle", typeof(IAssetBundleResource));
            Check(again.BaseProvided && FirebaseBoundary.Fetches.Count == 1, "캐시된 주소는 URL 요청 없이 기본 프로바이더가 처리한다");
            return Describe(request);
        });

        Run("AssetBundle 프로바이더는 URL을 받은 뒤에도 번들 캐시 키를 바꾸지 않고 받은 URL로 내려받는다", () =>
        {
            var first = Provide<FirebaseStorageAssetBundleProvider>("gs://bucket/bundle/key.bundle", typeof(IAssetBundleResource));
            FirebaseBoundary.ResolvePending();
            var again = Provide<FirebaseStorageAssetBundleProvider>("gs://bucket/bundle/key.bundle", typeof(IAssetBundleResource));
            string trace = "firstKey=" + first.CacheKey + " againKey=" + again.CacheKey + " firstUrl=" + first.BaseUrl + " againUrl=" + again.BaseUrl;

            Check(again.CacheKey == first.CacheKey, "URL을 받기 전과 뒤의 요청이 같은 캐시 키를 쓴다 (" + trace + ")");
            Check(first.BaseUrl == "https://storage.test/bucket/bundle/key.bundle" && again.BaseUrl == first.BaseUrl, "두 요청 모두 받은 URL로 내려받는다 (" + trace + ")");
            return trace;
        });

        Run("AssetBundle 프로바이더는 URL을 받기 전에 겹친 같은 주소 요청을 모두 기본 프로바이더로 넘긴다", () =>
        {
            var first = Provide<FirebaseStorageAssetBundleProvider>("gs://bucket/bundle/overlap.bundle", typeof(IAssetBundleResource));
            var second = Provide<FirebaseStorageAssetBundleProvider>("gs://bucket/bundle/overlap.bundle", typeof(IAssetBundleResource));
            FirebaseBoundary.ResolvePending();
            string trace = "first=" + Describe(first) + " url=" + first.BaseUrl + " second=" + Describe(second) + " url=" + second.BaseUrl;

            Check(first.BaseProvided && first.BaseUrl == "https://storage.test/bucket/bundle/overlap.bundle", "앞선 요청을 받은 URL로 읽는다 (" + trace + ")");
            Check(second.BaseProvided && second.BaseUrl == "https://storage.test/bucket/bundle/overlap.bundle", "뒤의 요청도 받은 URL로 읽는다 (" + trace + ")");
            return trace;
        });

        Run("AssetBundle 프로바이더는 URL 요청 실패를 원래 예외로 넘기고 주소를 gs://로 남긴다", () =>
        {
            var request = Provide<FirebaseStorageAssetBundleProvider>("gs://bucket/bundle/failed.bundle", typeof(IAssetBundleResource));
            FirebaseBoundary.Fetches[0].Source.SetException(new InvalidOperationException("bundle url failed"));
            FirebaseBoundary.RunMainThread();
            Check(request.CompleteCount == 1 && !request.Status && Carries(request, "bundle url failed"), "URL 오류로 실패한다 (" + Describe(request) + ")");
            Check(FirebaseBoundary.Provided.Count == 0, "내부 로드를 시작하지 않는다");
            Check(Transform("gs://bucket/bundle/failed.bundle") == "gs://bucket/bundle/failed.bundle", "실패한 주소는 바꾸지 않는다");
            return Describe(request);
        });
    }

    private static FirebaseBoundary.Request Provide<TProvider>(string internalId, Type resourceType, params IResourceLocation[] dependencies)
    {
        var provider = Addressables.ResourceManager.ResourceProviders.OfType<TProvider>().Single();
        var location = new FirebaseBoundary.Location
        {
            InternalId = internalId,
            ResourceType = resourceType,
            Data = "data:" + internalId,
            PrimaryKey = "key:" + internalId,
            Dependencies = new List<IResourceLocation>(dependencies)
        };
        // 실제 ResourceManager는 프로바이더를 부르기 전에 번들 op 캐시 키를 TransformInternalId로 정한다.
        var request = new FirebaseBoundary.Request { Location = location, RequestedType = resourceType, CacheKey = Addressables.ResourceManager.TransformInternalId(location) };
        ((ResourceProviderBase)(object)provider).Provide(new ProvideHandle(request));
        return request;
    }

    private static bool Carries(FirebaseBoundary.Request request, string message)
    {
        return request.Exception != null && request.Exception.ToString().Contains(message);
    }

    private static string Describe(FirebaseBoundary.Request request)
    {
        return "completes=" + request.CompleteCount + " status=" + request.Status + " base=" + request.BaseProvided
            + " result=" + (request.Result?.GetType().Name ?? "null") + " error=" + (request.Exception?.GetType().Name ?? "null") + ":" + request.Exception?.GetBaseException().Message;
    }
}
