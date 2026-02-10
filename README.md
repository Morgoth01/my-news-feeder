<h1 align="center">My News Feeder</h1>

<div align="center"><img width="256" height="256" alt="mynewsfeeder" src="https://github.com/user-attachments/assets/8a0d0642-4696-47bb-9a1a-643ee5fc4ac0" /></div>


<div align="center">My News Feeder is a lightweight RSS/Atom reader with integrated ad blocking.

Dive into your favorite feeds without distractions!</div>
<br/>
<br/>

**.NET 9.0 Desktop Runtime (v9.0.9 or later)** is required to run this application.  
[Download .NET 9.0 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
<br/>

Find the latest release and notes here:
**https://github.com/Morgoth01/my-news-feeder/releases**


## Guide

### How to run the app
1. Download the latest release
2. Extract the ZIP and run MyNewsFeeder.exe

### Restore settings and feeds
Copy `settings.json` and `feeds.json` from the old release into the same folder as the new version (next to MyNewsFeeder.exe)

### Manage Feeds

1. Click Manage Feeds in the toolbar.
2. Click Add Feed, paste the RSS/Atom URL and click Save.
3. Enable or disable individual feeds as needed.

### Add a new category
1. Click Manage Feeds in the toolbar.
2. Enter a name for the category and click Add.
3. You can now assign the newly created category to your feeds.

### Group feeds by category
Enable ”Group feeds by category” to reorder your feeds from the same category to appear together while respecting drag-and-drop ordering.

### Remove a category
1. Click Manage Feeds in the toolbar.
2. Select a category
3. Click on the bin icon or the Remove Selected Category button
4. Assigned feeds will be reassigned to the Default category

### Enable/Disable Ad Blocker
Open Settings → toggle Enable AdBlocker → click Save Settings.

The ad blocker uses multiple filter lists (see below).

### Advertisement filtering
Open Settings → toggle “Hide advertisement articles” → click Save Settings. Add or edit keywords in the “Advertisement keywords” box (one per line).

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
Enter a keyword and click enter or the refresh button to filter your feeds

## Features
* Add and manage unlimited RSS/Atom feeds
* Import/Export feeds
* Context menu on articles: pin, read later, mark unread, copy link.
* Built-in ad blocker with filter lists
* Filter feeds
* User-configurable auto-refresh interval
* Native dark mode support per site (if available)
* "Always-On" browser integration for seamless reading
* Drag-to-resize layout with persistent preferences
* Cache cleanup

### Ad Blocker Filter Lists
By default, the ad blocker loads these popular filter lists:

* AdGuard Base – Core ad-blocking rules for banners and pop-ups.
* EasyList – Removes most ads on international sites.
* EasyPrivacy – Blocks tracking scripts and web bugs.
* Fanboy’s Annoyances – Hides overlays, cookie notices, and social widgets.
* la–StevenBlackHosts – DNS-level hosts file blocking ads, malware, and trackers.
* uBlock Origin Filters – Extra rules from the uBlock Origin community.

You can add custom domains or hosts in adblocker_hosts.txt.

### FAQ
#### Why is the first webview2 load slower?
The first load after starting the app is slower because WebView2 has to start its browser engine, initialize the profile and caches, and set up networking. After that, the engine stays warm and cached, so the next pages load much faster.

---

Enjoy streamlined, distraction-free news reading with My News Feeder!

#### Dark Mode
<img width="3840" height="2076" alt="image" src="https://github.com/user-attachments/assets/1ceb26e0-a529-4094-a429-b79db342b191" />

#### Light Mode
<img width="3840" height="2076" alt="image" src="https://github.com/user-attachments/assets/cbccfe38-d51a-4880-81db-948af19dcc07" />

#### Toggle for compact cards
<img width="3840" height="2076" alt="image" src="https://github.com/user-attachments/assets/2e52d768-eb22-4821-8360-e6050e10b1ff" />

#### Settings
<img width="671" height="1121" alt="image" src="https://github.com/user-attachments/assets/ea1f01b6-8cfe-40af-8dde-9590db7c4af7" />

#### Filter feeds
<img width="1723" height="1223" alt="image" src="https://github.com/user-attachments/assets/d2f00688-a9af-4b5e-8f78-9b4be48e607e" />

#### Filter feeds by keyword
<img width="1609" height="1417" alt="image" src="https://github.com/user-attachments/assets/49899e2d-1ba3-488c-9c9d-e9bf2c81b14d" />

#### Article context menu
<img width="1635" height="1572" alt="image" src="https://github.com/user-attachments/assets/81e8a19c-217f-4614-9c1a-08dd1cc493eb" />

#### Feed Manager
<img width="2510" height="1549" alt="image" src="https://github.com/user-attachments/assets/a1877609-e9bf-4dd6-8133-b3710df45193" />

## Application Libraries

| Library                    | Purpose                                    | License                          | Link                                                                 |
|----------------------------|--------------------------------------------|----------------------------------|----------------------------------------------------------------------|
| MaterialDesignInXamlToolkit| UI theme and controls for WPF              | MIT                              | https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit  |
| Microsoft.Web.WebView2     | Embedded Edge-based browser                | Microsoft Software License Terms | https://developer.microsoft.com/microsoft-edge/webview2/             |
| System.Text.Json           | JSON serialization/deserialization         | MIT                              | https://www.nuget.org/packages/System.Text.Json/                     |
| Microsoft.Extensions.Http  | HTTP client factory and helpers            | MIT                              | https://www.nuget.org/packages/Microsoft.Extensions.Http/            |
| HtmlAgilityPack            | HTML parser                                | MIT                              | https://www.nuget.org/packages/HtmlAgilityPack/                      |

## Ad Blocker Filter Lists

| Filter List                  | License                        | Description                                            |
|------------------------------|--------------------------------|--------------------------------------------------------|
| AdGuardBase.txt              | GPLv3                          | Core ad-blocking rules for banners and pop-ups         |
| EasyList.txt                 | GPLv3 / CC BY-SA 3.0           | Removes most ads on international websites             |
| EasyPrivacy.txt              | GPLv3 / CC BY-SA 3.0           | Blocks tracking scripts and web bugs                   |
| FanboysAnnoyances.txt        | CC BY 3.0                      | Hides overlays, cookie notices, and social widgets     |
| la–StevenBlackHosts.txt      | CC BY 3.0                      | DNS-level hosts file blocking ads, malware, trackers   |
| uBlockOriginFilters.txt      | GPLv3                          | Additional rules from the uBlock Origin community      |
