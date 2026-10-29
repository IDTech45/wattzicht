# Wattzicht – website en installateursportal

Vue 3 + Vite + PrimeVue 4 + Vue Router + Pinia, TypeScript.

## Ontwikkelen

```bash
npm install
npm run dev        # http://localhost:5173, /api wordt doorgestuurd naar http://localhost:8080
```

## Bouwen

```bash
npm run build      # typecheck (vue-tsc) + productiebuild in dist/
npm run preview    # dist/ lokaal bekijken
```

## Docker

```bash
docker build -t wattzicht-frontend .
docker run -p 8081:80 wattzicht-frontend
```

De nginx-config serveert de SPA met fallback naar `index.html` en stuurt `/api/` door naar `http://api:8080` (de servicenaam in `docker-compose.yml`).

## Structuur

```
src/
├── api/client.ts        fetch-wrapper met Bearer-token + typed endpoints
├── stores/auth.ts       JWT + installateur in Pinia en localStorage
├── stores/leads.ts      demo-aanvragen (POST /api/leads) met offline-fallback
├── router/              routes + auth-guard voor /portal
├── components/          SiteNav, SiteFooter, EnergyFlowIllustration, BrandBadge, StatTile, StatusDot, PhonePreview
├── views/               Home, Installateurs, App, Privacy, 404
└── views/portal/        Login, PortalLayout, Households, HouseholdDetail, Branding
```

Routes: `/`, `/installateurs`, `/app`, `/privacy`, `/portal/login`, `/portal`, `/portal/households/:id`, `/portal/branding`.

## Huisstijl

Kleuren en typografie staan als CSS-variabelen in `src/style.css` (uit `brand/BRAND.md`). Het PrimeVue Aura-preset in `src/main.ts` gebruikt `sun` (#F59E0B) als primary en slate als surface. Dark mode volgt `prefers-color-scheme`.

## API-verwachtingen

| Methode | Pad | Gebruik |
| --- | --- | --- |
| POST | `/api/auth/login` | `{ email, password }` → `{ token, installer }` |
| GET | `/api/installer/households` | lijst met klanten en status |
| GET | `/api/installer/households/:id` | detail met `live`, `alerts`, `today`, `month` |
| GET/PUT | `/api/installer/branding` | `{ name, logoDataUrl, primaryColor, quoteUrl }` |
| POST | `/api/leads` | demo-aanvraag vanaf de site |

De types staan in `src/api/client.ts`.
