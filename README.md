# BookFinder

Book recommendations from your taste, plus free downloads from **legal** sources only
(Project Gutenberg, Internet Archive public-domain scans). For books still under copyright it gives you an
Open Library borrow link and points you to Libby/OverDrive or Hoopla.

- **Backend:** ASP.NET Core 8 minimal API (`src/BookFinder.Api`)
- **Frontend:** Angular 22, standalone + signals (`web/`)
- **Tests:** xUnit (`tests/BookFinder.Api.Tests`), Vitest (`web/`)

## Run (development)

Terminal 1, the API on http://localhost:5032:

    dotnet run --project src/BookFinder.Api --launch-profile http

Terminal 2, the Angular dev server on http://localhost:4200 (proxies `/api` to the API):

    cd web && npm install && npx ng serve

## Run (single app)

    cd web && npx ng build        # outputs into src/BookFinder.Api/wwwroot
    dotnet run --project src/BookFinder.Api --launch-profile http

Then open http://localhost:5032.

## Tests

    dotnet test
    cd web && npx ng test --watch=false

## Configuration

| Setting | Default | Purpose |
|---|---|---|
| `BookFinder:DataDir` | `<bin>/data` | preferences.json location |
| `BookFinder:LibraryDir` | `<DataDir>/library` | where downloaded books are saved |

Set them in `appsettings.json` or with environment variables (`BookFinder__DataDir=...`).

## API

| Method | Route | |
|---|---|---|
| GET | `/api/preferences` | your taste profile |
| POST/DELETE | `/api/preferences/books` | like a book (`{title}`) / remove (`?key=`) |
| POST/DELETE | `/api/preferences/{authors\|subjects\|seen}` | `{value}` / `?value=` |
| GET | `/api/recommendations?count=20` | ranked recommendations with reasons |
| GET | `/api/free?title=&author=` | free legal copies, or a borrow link |
| GET/POST | `/api/library` | list downloads / download `{url,title,format}` |
| GET | `/api/library/{file}` | open a downloaded book |

Downloads only go through for an https URL on gutenberg.org or archive.org, so the API can't be used as an open proxy.
