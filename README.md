# 🗺️ SocialMonster — Mongolian Social Media Location Monitor

An ASP.NET MVC web application built for **Sumbee.mn** that does two things:

1. **Visualizes** Facebook posts tagged with location mentions on an interactive map of Mongolia
2. **Manages** location-alias mappings, allowing admins to manually correct and enrich extracted location data

---

## 📁 Project Structure

```
SocialMonster/
├── Controllers/
│   ├── LocationController.cs   # All endpoints — data retrieval, map, editing
│   └── LocationFilter.cs       # Filter parameter model (date, region, search)
├── Models/
│   └── LocationViewModels.cs   # ViewModels for dashboard, list, edit, and map
├── DAL/
│   ├── SumbeeModel.Context.cs  # Entity Framework DbContext
│   └── SumbeeModel.tt          # EF entity definitions
├── Views/
│   └── Location/
│       └── Index.cshtml        # Main UI — map, table, filters, edit modals
├── App_Start/
│   └── RouteConfig.cs          # Default route → Location/Index
└── Web.config                  # DB connection string, .NET 4.7.2 config
```

---

## 🧱 Architecture

| Layer | Technology |
|-------|-----------|
| Web framework | ASP.NET MVC 5.2.9 (.NET 4.7.2) |
| ORM | Entity Framework 6.5.1 |
| Database | SQL Server (Express), database `SocialDataLocal` |
| Frontend | Bootstrap 5.3, jQuery 3.7, DataTables 1.13 |
| Mapping | Leaflet.js 1.9.4 + OpenStreetMap tiles |
| Serialization | Newtonsoft.Json 13.0 |

---

## 🗄️ Database Schema

| Table | Purpose |
|-------|---------|
| `Facebook.Posts` | Raw Facebook post content and metadata |
| `Social.Contents` | Hierarchical location taxonomy (Levels 1–6) |
| `Social.Content.Post` | Post ↔ location alias mappings |
| `Social.Location.More` | Lat/lon coordinates for location nodes |

**Location hierarchy in `Social.Contents`:**

| Level | Type | Example |
|-------|------|---------|
| 2 | Province / Aimag | `Улаанбаатар` |
| 3 | District / Düüreg | `Сүхбаатар дүүрэг` |
| 4 | Sub-district | `7-р хороолол` |
| 5 | Canonical location | `7-р хороолол` (canonical form) |
| 6 | Alias | `7-р хүрээ`, `7-р районе` |

---

## ⚙️ Requirements

- .NET Framework 4.7.2
- SQL Server Express (localhost\SQLEXPRESS)
- Visual Studio 2019+ or MSBuild
- Database `SocialDataLocal` populated via the [Migration toolkit](../../README.md)

---

## 🚀 Setup

### 1. Configure the database connection

In `Web.config`, update the connection string to point to your SQL Server instance:

```xml
<connectionStrings>
  <add name="SocialDataLocalEntities"
       connectionString="...Data Source=YOUR_SERVER;Initial Catalog=SocialDataLocal;..."
       providerName="System.Data.SqlClient" />
</connectionStrings>
```

> ⚠️ **Security note:** Do not commit credentials to source control. Use environment-specific transforms (`Web.Release.config`) or user secrets.

### 2. Restore packages and build

```bash
nuget restore SocialMonster.sln
msbuild SocialMonster.sln /p:Configuration=Release
```

Or open `SocialMonster.sln` in Visual Studio and press **F5** to run.

### 3. Seed location data first

Before running the web app, populate the location hierarchy and post matches using the Migration toolkit:

```bash
cd Migration
python insert.py   # Seed location hierarchy
python new.py      # Run location extraction on posts
```

---

## 🌐 Features

### Interactive Map
- Leaflet.js map centered on Mongolia (`47.9186, 106.9170`)
- Circle markers sized proportionally to post frequency
- Top 5 locations highlighted; supports 200+ simultaneous markers
- Click a marker to zoom in and filter the post table

### Post Table
- DataTables-powered paginated list of Facebook posts
- Columns: Account, Post content, Date, Detected location, Source URL, Actions
- "Only with location" toggle to filter to matched posts only

### Filtering
- **Category** — search by location mention or account name
- **Text search** — with autocomplete showing canonical name + hierarchy
- **Date range** — start/end date pickers
- **Province / District** — cascading geographic dropdowns

### Statistics Panel
- Total posts in DB
- Posts with at least one location match
- Unique locations count
- Top locations ranked by frequency with percentage breakdown

### Admin Editing
- Click **Edit** on any post to open the edit modal
- View full post text alongside all detected alias → canonical mappings
- **Remove** incorrect mappings with one click
- **Add** new mappings: type an alias token, search for a canonical location, link them

---

## 🔌 API Endpoints

All endpoints are under `/Location/` and return JSON.

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `GetLocationStatistics` | GET | Dashboard counts and top locations |
| `GetLocationList` | GET | Paginated post list (DataTables server-side) |
| `GetMapLocations` | GET | Lat/lon + count for map markers |
| `GetLocationFilters` | GET | Available location filter options |
| `GetProvinces` | GET | Level 2 province list |
| `GetDistricts?provinceId=` | GET | Level 3 districts for a province |
| `GetLocationBounds?id=` | GET | Bounding box for map auto-zoom |
| `GetPostDateRange` | GET | Min/max post dates in the DB |
| `SearchAliasHierarchy?q=` | GET | Autocomplete — alias + canonical hierarchy |
| `GetEditDetails?postId=` | GET | Post content + current mappings for edit modal |
| `AddLocationMapping` | POST | Create or link an alias → canonical mapping |
| `RemoveLocationMapping` | POST | Delete a post-location mapping |

---

## 📝 Notes

- The default route points to `Location/Index` — the app opens directly to the main dashboard.
- `MultipleActiveResultSets=True` is required in the connection string for EF concurrent queries.
- All Mongolian Cyrillic text is handled natively; no additional encoding configuration is needed.
- Location data must be seeded by the Migration toolkit before the map or statistics will show meaningful results.
