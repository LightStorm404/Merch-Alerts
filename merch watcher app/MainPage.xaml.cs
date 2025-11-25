using System;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace merch_watcher_app;

public partial class MainPage : ContentPage
{
    private bool _isRunning = false;

    public MainPage()
    {
        InitializeComponent();

        //
        // ───── LISTEN FOR MADISON LOG MESSAGES ─────
        //
        MessagingCenter.Subscribe<object, string>(this, "MadisonUpdateText", (sender, message) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MadisonLogLabel.Text = message;
            });
        });

        MessagingCenter.Subscribe<object, string>(this, "MadisonUpdateTime", (sender, time) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MadisonTimeLabel.Text = time;
            });
        });

        //
        // ───── LISTEN FOR TRAVIS LOG MESSAGES ─────
        //
        MessagingCenter.Subscribe<object, string>(this, "TravisUpdateText", (sender, message) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TravisLogLabel.Text = message;
            });
        });

        MessagingCenter.Subscribe<object, string>(this, "TravisUpdateTime", (sender, time) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TravisTimeLabel.Text = time;
            });
        });

        // Initial placeholders
        MadisonLogLabel.Text = "no checks yet";
        TravisLogLabel.Text = "no checks yet";
        MadisonTimeLabel.Text = "-";
        TravisTimeLabel.Text = "-";
    }

    // ─────────────────────────────────────────────
    // MADISON BUTTONS
    // ─────────────────────────────────────────────

    private void OnMadisonStartClicked(object sender, EventArgs e)
    {
#if ANDROID
        StartWatcherService();
        MadisonLogLabel.Text = "waiting for first check...";
        MadisonTimeLabel.Text = "-";
#else
        MadisonLogLabel.Text = "Android only";
#endif
    }

    private void OnMadisonStopClicked(object sender, EventArgs e)
    {
#if ANDROID
        StopWatcherService();
        MadisonLogLabel.Text = "stopped";
        MadisonTimeLabel.Text = "-";
#else
        MadisonLogLabel.Text = "Android only";
#endif
    }

    // ─────────────────────────────────────────────
    // TRAVIS BUTTONS
    // ─────────────────────────────────────────────

    private void OnTravisStartClicked(object sender, EventArgs e)
    {
#if ANDROID
        StartWatcherService();
        TravisLogLabel.Text = "waiting for first check...";
        TravisTimeLabel.Text = "-";
#else
        TravisLogLabel.Text = "Android only";
#endif
    }

    private void OnTravisStopClicked(object sender, EventArgs e)
    {
#if ANDROID
        StopWatcherService();
        TravisLogLabel.Text = "stopped";
        TravisTimeLabel.Text = "-";
#else
        TravisLogLabel.Text = "Android only";
#endif
    }

    // ─────────────────────────────────────────────
    // SERVICE CONTROL
    // ─────────────────────────────────────────────

#if ANDROID
    private void StartWatcherService()
    {
        if (_isRunning)
            return;

        var context = Android.App.Application.Context;
        var intent = new Android.Content.Intent(context, typeof(WatcherService));

        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            context.StartForegroundService(intent);
        else
            context.StartService(intent);

        _isRunning = true;
    }

    private void StopWatcherService()
    {
        if (!_isRunning)
            return;

        var context = Android.App.Application.Context;
        var intent = new Android.Content.Intent(context, typeof(WatcherService));
        context.StopService(intent);

        _isRunning = false;
    }
#endif
}
