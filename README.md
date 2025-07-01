# My News Feeder

My News Feeder is a lightweight RSS/Atom reader with integrated ad blocking.

Dive into your favorite feeds without distractions!

## Guide
### Manage Feeds

1. Click Manage Feeds in the toolbar.
2. Click Add Feed, paste the RSS/Atom URL and click Save.
3. Enable or disable individual feeds as needed.


### Enable/Disable Ad Blocker
Open Settings → toggle Enable AdBlocker → click Save Settings.

The ad blocker uses multiple filter lists (see below).

### Set Auto-Refresh Interval
1. In Settings, check Auto Refresh.
2. Choose an interval from the dropdown: 5, 10, 15, 20, 30, 45, or 60 minutes.
3. Click Save Feed Settings to start the timer.

### Max articles per feed
You can set the max articles per feed to control how many posts show up. This lets you decide how many latest articles you want to see in your feed at once.

### Toggle Dark Mode & Always-On Content
* Dark Mode respects each website’s native theme via the integrated browser.
* Show Content Always-On keeps the browser visible and reloads pages on each article selection.

### Filter feeds by Keyword
Enter a keyword and click refresh to filter your feeds

## Features
* Add and manage unlimited RSS/Atom feeds
* Import/Export feeds
* Built-in ad blocker with filter lists
* Filter feeds by keyword
* User-configurable auto-refresh interval
* Native dark mode support per site (if available)
* "Always-On" browser integration for seamless reading
* Drag-to-resize layout with persistent preferences
* Cache cleanup

### Ad Blocker Filter Lists
By default, the ad blocker loads these popular filter lists:

* AdGuard Base – Core ad-blocking rules for banners and pop-ups.
* ClearURLs – Strips tracking parameters from URLs.
* EasyList – Removes most ads on international sites.
* EasyPrivacy – Blocks tracking scripts and web bugs.
* Fanboy’s Annoyances – Hides overlays, cookie notices, and social widgets.
* NoCoin List – Prevents in-browser cryptocurrency mining.
* la–StevenBlackHosts – DNS-level hosts file blocking ads, malware, and trackers.
* uBlock Origin Filters – Extra rules from the uBlock Origin community.

You can add custom domains or hosts in adblocker_hosts.txt.

## How It Works
* Startup: Loads your settings and feeds and initializes the integrated browser.
* Fetch Articles: Click Refresh or wait for auto-refresh; articles appear grouped by feed.
* Read: Select an article to view it in the integrated WebView2 browser with ads removed.
* Customize: Change settings anytime: dark mode, refresh interval and ad blocker.
* Maintain: Clear cache

Enjoy streamlined, distraction-free news reading with My News Feeder!

Dark Mode
<img width="1153" alt="Ze037zlvLh" src="https://github.com/user-attachments/assets/31a7b860-3008-47cd-bd9d-4294582df5df" />

Light Mode
<img width="1153" alt="cinwR4FdWX" src="https://github.com/user-attachments/assets/525bd504-759e-4a05-bae0-036834e945e6" />

Integrated Browser
<img width="1376" alt="3M77CjghVg" src="https://github.com/user-attachments/assets/f046ca95-a706-4041-b86c-14dc12c51b27" />

Settings

<img width="313" alt="gHaJbnFoD8" src="https://github.com/user-attachments/assets/b0b71196-ed36-4f5e-a0e8-2ade945a2d93" />

Filter feeds by Keyword
<img width="1376" alt="RIwECWTp5l" src="https://github.com/user-attachments/assets/ee2733c8-4109-40f0-801a-d9bf76be3c02" />

Feed Manager

<img width="591" alt="KdRCWnq5Zu" src="https://github.com/user-attachments/assets/7f1cdd78-1bda-4798-93b1-4d93cf557ccf" />

## Application Libraries

| Library                    | Purpose                                    | License                          | Link                                                                 |
|----------------------------|--------------------------------------------|----------------------------------|----------------------------------------------------------------------|
| MaterialDesignInXamlToolkit| UI theme and controls for WPF              | MIT                              | https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit   |
| Microsoft.Web.WebView2     | Embedded Edge-based browser                | Microsoft Software License Terms | https://developer.microsoft.com/microsoft-edge/webview2/             |
| System.Text.Json           | JSON serialization/deserialization         | MIT                              | https://www.nuget.org/packages/System.Text.Json/                     |
| Microsoft.Extensions.Http  | HTTP client factory and helpers            | MIT                              | https://www.nuget.org/packages/Microsoft.Extensions.Http/            |

## Ad Blocker Filter Lists

| Filter List                  | License                        | Description                                            |
|------------------------------|--------------------------------|--------------------------------------------------------|
| AdGuardBase.txt              | GPLv3                          | Core ad-blocking rules for banners and pop-ups         |
| ClearURLs.txt                | GPLv3                          | Strips tracking parameters from URLs                   |
| EasyList.txt                 | GPLv3 / CC BY-SA 3.0           | Removes most ads on international websites             |
| EasyPrivacy.txt              | GPLv3 / CC BY-SA 3.0           | Blocks tracking scripts and web bugs                   |
| FanboysAnnoyances.txt        | CC BY 3.0                      | Hides overlays, cookie notices, and social widgets     |
| NoCoinList.txt               | MIT                            | Prevents in-browser cryptocurrency mining              |
| la–StevenBlackHosts.txt      | CC BY 3.0                      | DNS-level hosts file blocking ads, malware, trackers   |
| uBlockOriginFilters.txt      | GPLv3                          | Additional rules from the uBlock Origin community      |
