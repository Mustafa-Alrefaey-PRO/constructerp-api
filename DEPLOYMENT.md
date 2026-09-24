# Deploying ConstructERP

Written for whoever sets up the Azure resources — you only need to do this once.

Two pieces live in two repositories:

| Piece | Repo | Hosted on |
| --- | --- | --- |
| Angular frontend | `was61664/constructerp-prototype` | GitHub Pages (static) |
| .NET API + SQL Server | `Mustafa-Alrefaey-PRO/constructerp-api` | Azure App Service + Azure SQL |

**GitHub Pages cannot host the API.** It serves static files only — no .NET
process, no database. That is why the API needs somewhere else to live, and
why the frontend is built so it works without one: with no API configured it
runs on seed data and skips sign-in entirely, which is the public demo.

---

## 1. Create the Azure resources

In the portal or with the CLI. Names are yours; keep them consistent with the
secrets in step 3.

```bash
az group create --name constructerp --location westeurope

# App Service. B1 is the cheapest tier that stays warm; F1 (free) sleeps and
# makes the first request after idle take ~30 seconds.
az appservice plan create --name constructerp-plan --resource-group constructerp --sku B1 --is-linux
az webapp create --name constructerp-api --resource-group constructerp \
  --plan constructerp-plan --runtime "DOTNETCORE:10.0"

# Azure SQL. The free tier gives 32 GB and 100k vCore-seconds a month.
az sql server create --name constructerp-sql --resource-group constructerp \
  --admin-user erpadmin --admin-password '<a strong password>'
az sql db create --name ConstructErp --server constructerp-sql \
  --resource-group constructerp --edition GeneralPurpose --compute-model Serverless \
  --family Gen5 --capacity 1 --auto-pause-delay 60

# Let App Service reach the database.
az sql server firewall-rule create --resource-group constructerp \
  --server constructerp-sql --name allow-azure \
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
```

That last rule means "Azure services", not "the whole internet" — the
0.0.0.0/0.0.0.0 pair is Azure's special case for it. The deploy workflow opens
and closes a separate, temporary rule for the GitHub runner when it applies
migrations.

## 2. Configure the App Service

Application settings, not files. Nested keys use a double underscore.

| Setting | Value |
| --- | --- |
| `ConnectionStrings__ErpDatabase` | `Server=tcp:constructerp-sql.database.windows.net,1433;Database=ConstructErp;User ID=erpadmin;Password=<password>;Encrypt=True;TrustServerCertificate=False;` |
| `Jwt__SigningKey` | **At least 32 characters, random, and generated fresh.** Never the development value in `appsettings.Development.json`. |
| `Cors__AllowedOrigins__0` | `https://was61664.github.io` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

Generate the signing key with `openssl rand -base64 48`. Startup throws if it
is missing or shorter than 32 characters — deliberately, because a fallback
default is how a signing key reaches production unnoticed, and anyone holding
the source could then mint an administrator token.

### The first administrator

A freshly migrated database has no users, and every route needs
authentication — so nobody can sign in and there is no endpoint to create the
first account. Set these two for the first deploy only:

| Setting | Value |
| --- | --- |
| `Seed__AdminEmail` | your real address |
| `Seed__AdminPassword` | a strong password you will change |

On startup the API creates that one administrator (and the internal
organization) if no such user exists. It does **not** seed demo projects,
equipment or the carrier accounts — those are Development only.

**Delete both settings once you have signed in.** They are not needed again,
and a password sitting in application settings is a password in a place people
can read.

## 3. GitHub secrets on the API repo

Settings → Secrets and variables → Actions.

| Secret | Where it comes from |
| --- | --- |
| `AZURE_CREDENTIALS` | `az ad sp create-for-rbac --name constructerp-deploy --role contributor --scopes /subscriptions/<id>/resourceGroups/constructerp --sdk-auth` — paste the whole JSON |
| `AZURE_RESOURCE_GROUP` | `constructerp` |
| `AZURE_WEBAPP_NAME` | `constructerp-api` |
| `AZURE_SQL_SERVER` | `constructerp-sql` (no domain suffix) |
| `AZURE_SQL_DATABASE` | `ConstructErp` |
| `AZURE_SQL_USER` | `erpadmin` |
| `AZURE_SQL_PASSWORD` | the password from step 1 |

Then run **Deploy API to Azure App Service** from the Actions tab. It runs the
full test suite against a real SQL Server first, applies migrations as an
idempotent script, deploys, and fails if `/health` does not return 200.

## 4. Point the frontend at it

On the **frontend** repo: Settings → Secrets and variables → Actions →
Variables → New repository variable.

| Variable | Value |
| --- | --- |
| `API_BASE_URL` | `https://constructerp-api.azurewebsites.net` |

It must be `https`. GitHub Pages is served over https, and a browser blocks a
plain-http request from an https page as mixed content — the site would look
fine while every request silently failed.

Leave the variable unset and the published site stays the self-contained demo:
seed data, no sign-in, nothing to break.

Re-run the Pages workflow after setting it. The value is baked in at build
time, so changing it needs a rebuild, not a restart.

---

## Checking it worked

```bash
curl https://constructerp-api.azurewebsites.net/health
# {"status":"ok"}

curl -X POST https://constructerp-api.azurewebsites.net/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"you@example.com","password":"<the seeded password>"}'
# a token pair
```

Then open the Pages site and sign in.

## When something is wrong

**Every API call fails from the browser but `curl` works.** CORS. Check
`Cors__AllowedOrigins__0` is exactly `https://was61664.github.io` — the origin
only, no path and no trailing slash.

**Requests fail silently with no response.** Mixed content: `API_BASE_URL` is
`http`, not `https`.

**500 on the first request after a quiet period.** Azure SQL serverless
auto-pauses; the first connection wakes it and can time out. Raise
`--auto-pause-delay` or move off serverless.

**The deploy fails applying migrations.** The runner's firewall rule did not
open. Check the service principal has `contributor` on the resource group.

**Login returns 500.** `Jwt__SigningKey` is missing or under 32 characters.
The app logs the reason on startup.
