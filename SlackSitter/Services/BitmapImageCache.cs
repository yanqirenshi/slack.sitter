using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SlackSitter.Services
{
    /// <summary>
    /// BitmapImage をURL単位でキャッシュし、同じ画像の重複デコードを防ぐ。
    /// WeakReference を使用しているため、UIツリーから外れた画像はGC対象になる。
    /// </summary>
    public sealed class BitmapImageCache
    {
        private readonly Dictionary<string, WeakReference<BitmapImage>> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 指定URIの BitmapImage をキャッシュから取得、なければ新規作成して返す。
        /// アバター画像向け（DecodePixelWidth=64）。
        /// </summary>
        public BitmapImage GetOrCreate(Uri uri)
        {
            return GetOrCreateInternal(uri, decodePixelWidth: 64);
        }

        /// <summary>
        /// 絵文字画像向け（DecodePixelWidth=24）。
        /// </summary>
        public BitmapImage GetOrCreateEmoji(Uri uri)
        {
            return GetOrCreateInternal(uri, decodePixelWidth: 24);
        }

        /// <summary>
        /// キャッシュをクリアする。リフレッシュ時に呼び出す。
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
        }

        private BitmapImage GetOrCreateInternal(Uri uri, int decodePixelWidth)
        {
            var key = uri.AbsoluteUri;

            if (_cache.TryGetValue(key, out var weakRef) && weakRef.TryGetTarget(out var cached))
            {
                return cached;
            }

            var bitmap = new BitmapImage
            {
                DecodePixelWidth = decodePixelWidth,
                DecodePixelType = DecodePixelType.Logical,
                UriSource = uri
            };

            _cache[key] = new WeakReference<BitmapImage>(bitmap);
            return bitmap;
        }
    }
}
