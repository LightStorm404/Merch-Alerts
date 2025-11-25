using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using System.Text.RegularExpressions;

namespace merch_watcher_app;

[Service(Exported = false, ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
public class WatcherService : Service
{
    private const int ServiceNotificationId = 1001;
    private const string ChannelId = "madison_watcher_channel";

    private readonly Dictionary<string, string> _products = new()
    {
        
        // Madison items URLs:
        { "locket vinyl", "https://www.onrpt.store/collections/madison-beer/products/locket-lp" },
        { "locket cd", "https://www.onrpt.store/collections/madison-beer/products/locket-cd" },
        { "limited edition locket necklace and cd - bundle", "https://www.onrpt.store/collections/madison-beer/products/limited-edition-locket-necklace-and-cd-bundle" },
        { "limited edition locket necklace and cd + signed insert", "https://www.onrpt.store/collections/madison-beer/products/limited-edition-locket-necklace-and-cd-signed" },
        { "locket music bundle", "https://www.onrpt.store/collections/madison-beer/products/locket-music-bundle" },
        { "limited edition locket vinyl + signed insert", "https://www.onrpt.store/collections/madison-beer/products/signed-insert-locket-vinyl" },
        { "locket cassette", "https://www.onrpt.store/collections/madison-beer/products/locket-cassette" },
        { "limited edition locket necklace", "https://www.onrpt.store/collections/madison-beer/products/locket" },
        { "locket tee", "https://www.onrpt.store/collections/madison-beer/products/limited-edition-locket-tee" },
        { "locket tee + cd + signed insert", "https://www.onrpt.store/collections/madison-beer/products/limited-edition-locket-tee-and-cd-signed-insert" },
        { "locket tee + cd", "https://www.onrpt.store/collections/madison-beer/products/limited-edition-locket-tee-and-cd" },

        // Travis itrems URLs:
        { "utopia tee","https://travis-store-link-here" },
        { "tour hoodie", "https://travis-store-link-here-2" },

    };

    // which artist each product belongs to
    private readonly Dictionary<string, string> _productArtist = new()
{
        // Madison products
        { "locket vinyl", "madison" },
        { "locket cd", "madison" },
        { "limited edition locket necklace and cd - bundle", "madison" },
        { "limited edition locket necklace and cd + signed insert", "madison" },
        { "locket music bundle", "madison" },
        { "limited edition locket vinyl + signed insert", "madison" },
        { "locket cassette", "madison" },
        { "limited edition locket necklace", "madison" },
        { "locket tee", "madison" },
        { "locket tee + cd + signed insert", "madison" },
        { "locket tee + cd", "madison" },

        // Travis products
        { "utopia tee", "travis" },
        { "tour hoodie", "travis" },
};


    private enum ProductStatus
    {
        SoldOut,
        PreOrder,
        InStock
    }

    private readonly Dictionary<string, ProductStatus> _lastStatus = new();
    private HttpClient? _client;
    private CancellationTokenSource? _cts;

    public override void OnCreate()
    {
        base.OnCreate();

        CreateNotificationChannel();

        var notif = BuildServiceNotification("Starting up alerts…");
        StartForeground(ServiceNotificationId, notif);

        _client = new HttpClient();
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

        foreach (var key in _products.Keys)
            _lastStatus[key] = ProductStatus.SoldOut;

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // Keep running until explicitly stopped or killed
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        _cts?.Cancel();
        _client?.Dispose();

        // Stop the foreground notification and show a "service stopped" info notification
        StopForeground(true);
        ShowStoppedNotification();

        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    private async Task LoopAsync(CancellationToken token)
    {
        if (_client == null)
            return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var kvp in _products)
                {
                    string name = kvp.Key;
                    string url = kvp.Value;

                    try
                    {
                        string html = await _client.GetStringAsync(url, token);
                        var status = GetStatus(html);
                        var previous = _lastStatus[name];

                        if (status != previous)
                        {
                            if (status == ProductStatus.PreOrder)
                            {
                                ShowChangeNotification(
                                    name,
                                    "is now available to PRE-ORDER.",
                                    url);
                            }
                            else if (status == ProductStatus.InStock)
                            {
                                ShowChangeNotification(
                                    name,
                                    "is now IN STOCK!",
                                    url);
                            }

                            _lastStatus[name] = status;
                        }

                        // send log line to UI
                        try
                        {
                            string artist = _productArtist.TryGetValue(name, out var a) ? a : "madison";

                            string line = $"{status} | {name}";
                            string time = DateTime.Now.ToString("HH:mm:ss");

                            Microsoft.Maui.Controls.Application.Current?.Dispatcher.Dispatch(() =>
                            {
                                if (artist == "madison")
                                {
                                    MessagingCenter.Send<object, string>(this, "MadisonUpdateText", line);
                                    MessagingCenter.Send<object, string>(this, "MadisonUpdateTime", time);
                                }
                                else if (artist == "travis")
                                {
                                    MessagingCenter.Send<object, string>(this, "TravisUpdateText", line);
                                    MessagingCenter.Send<object, string>(this, "TravisUpdateTime", time);
                                }
                            });
                        }
                        catch { }


                    }
                    catch
                    {
                        // ignore individual errors and keep loop running
                    }
                }

                if (DateTime.Now.Second == 0)
                {
                    UpdateServiceNotification();
                }

                await Task.Delay(2000, token);

            }
        }
        catch (TaskCanceledException)
        {
            // service stopped
        }
    }

    private static ProductStatus GetStatus(string htmlRaw)
    {
        if (string.IsNullOrWhiteSpace(htmlRaw))
            return ProductStatus.SoldOut;

        // work in lowercase so checks are easier
        string html = htmlRaw.ToLowerInvariant();

        // 1) Try to find the main product button.
        // This regex looks for a <button> whose attributes typically match
        // add-to-cart / product submit buttons.
        var buttonMatch = Regex.Match(
            html,
            "<button[^>]*(add-to-cart|product-form__submit|name=\"add\")[^>]*>(.*?)</button>",
            RegexOptions.Singleline);

        if (buttonMatch.Success)
        {
            // Group 2 = inner HTML of the button (text + maybe nested tags)
            string inner = buttonMatch.Groups[2].Value;

            // strip any tags inside the button, keep just text
            string textOnly = Regex.Replace(inner, "<.*?>", string.Empty)
                                   .Trim()
                                   .ToLowerInvariant();

            // Now decide based on the actual button label
            if (textOnly.Contains("sold out"))
                return ProductStatus.SoldOut;

            if (textOnly.Contains("pre-order") || textOnly.Contains("pre order"))
                return ProductStatus.PreOrder;

            // If it’s not sold out and not pre-order → treat it as in stock
            return ProductStatus.InStock;
        }

        // 2) Fallback: old heuristic, in case we didn’t find a button at all

        bool hasSoldOut =
            html.Contains("- sold out") ||
            html.Contains(">sold out<") ||
            html.Contains(" sold out ");

        bool hasPreOrderCopy =
            html.Contains("this product is currently on pre-order") ||
            html.Contains("this item is a recurring or deferred purchase") ||
            html.Contains("pre-order now") ||
            html.Contains("pre order now") ||
            html.Contains(" pre-order ") ||
            html.Contains(" pre order ");

        bool hasInStockButton =
            html.Contains("add to cart") ||
            html.Contains("add to bag") ||
            html.Contains("add to basket") ||
            html.Contains("buy now");

        if (hasInStockButton)
            return ProductStatus.InStock;

        if (hasPreOrderCopy)
            return ProductStatus.PreOrder;

        if (hasSoldOut)
            return ProductStatus.SoldOut;

        // default fallback
        return ProductStatus.InStock;
    }


    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                ChannelId,
                "Merch Watcher",
                NotificationImportance.Low);

            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.CreateNotificationChannel(channel);
        }
    }

    private Notification BuildServiceNotification(string text)
    {
        return new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Merch watcher")
            .SetContentText(text)
            .SetSmallIcon(Android.Resource.Drawable.StatSysDownloadDone)
            .SetOngoing(true)
            .Build();
    }

    private void ShowChangeNotification(string name, string message, string url)
    {
        var bigText = $"{name} {message}\n{url}";

        // Intent that opens the store page when you tap the notification
        var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(url));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);

        var pendingIntent = PendingIntent.GetActivity(
            this,
            0,
            intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent
        );

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle(name)
            .SetContentText(message)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(bigText))
            .SetSmallIcon(Android.Resource.Drawable.StatNotifyMore)
            .SetAutoCancel(true)
            .SetPriority((int)NotificationPriority.High)
            .SetContentIntent(pendingIntent);  // 👈 tap action

        var manager = NotificationManagerCompat.From(this);
        int id = (int)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        manager.Notify(id, builder.Build());
    }

    private void ShowStoppedNotification()
    {
        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Merch watcher")
            .SetContentText("Service stopped – watcher is no longer running.")
            .SetSmallIcon(Android.Resource.Drawable.StatNotifyError)
            .SetAutoCancel(true)
            .SetPriority((int)NotificationPriority.Low);

        var manager = NotificationManagerCompat.From(this);
        int id = (int)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        manager.Notify(id, builder.Build());
    }
    private void UpdateServiceNotification()
    {
        string time = DateTime.Now.ToString("HH:mm");
        string text = $"Alerts active… (last check {time})";

        var notif = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Merch watcher")
            .SetContentText(text)
            .SetSmallIcon(Android.Resource.Drawable.StatSysDownloadDone)
            .SetOngoing(true)
            .Build();

        var manager = NotificationManagerCompat.From(this);
        manager.Notify(ServiceNotificationId, notif);
    }
}

