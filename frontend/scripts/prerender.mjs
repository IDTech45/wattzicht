// Build-time prerender: bakt per route een eigen <head> (title, description, canonical, Open Graph,
// Twitter card en JSON-LD) in een eigen HTML-bestand.
//
// Een Vue-SPA levert anders één index.html voor élke URL. Gevolg: een crawler die geen JavaScript
// draait (de eerste ronde van Google, en de linkvoorbeelden van WhatsApp, LinkedIn en Facebook) ziet
// op /installateurs exact dezelfde titel en omschrijving als op de homepage. Dan deel je een link en
// staat er iets anders in het voorbeeld dan op de pagina.
//
// nginx serveert dist/<pad>/index.html via `try_files $uri $uri/ /index.html`, dus deze bestanden
// worden vanzelf opgepakt. Zelfde aanpak als bij LaadCash, met seo-routes.json als enige bron.
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const root = resolve(here, '..')
const dist = join(root, 'dist')

const seo = JSON.parse(readFileSync(join(root, 'seo-routes.json'), 'utf8'))
const template = readFileSync(join(dist, 'index.html'), 'utf8')

const esc = (s) =>
  s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

// Vervangt de inhoud van een <meta>, of zet hem erbij als hij nog niet bestaat. De inhoud wordt
// gematcht tot hetzelfde aanhalingsteken waarmee hij begon (\2); met [^"']* zou een apostrof in de
// tekst ("euro's") de match afkappen en bleef er een restje staan.
function setMeta(html, attr, key, content) {
  const re = new RegExp(`(<meta\\s+${attr}=["']${key}["']\\s+content=)(["'])[\\s\\S]*?\\2`, 'i')
  if (re.test(html)) return html.replace(re, `$1$2${esc(content)}$2`)
  return html.replace('</head>', `    <meta ${attr}="${key}" content="${esc(content)}" />\n  </head>`)
}

function setCanonical(html, url) {
  const re = /(<link\s+rel=["']canonical["']\s+href=)(["'])[\s\S]*?\2/i
  if (re.test(html)) return html.replace(re, `$1$2${esc(url)}$2`)
  return html.replace('</head>', `    <link rel="canonical" href="${esc(url)}" />\n  </head>`)
}

function render(route) {
  const desc = route.description || seo.defaults.description
  const url = seo.baseUrl + route.path
  const ogImage = seo.baseUrl + (route.ogImage || seo.defaults.ogImage)

  let html = template
  html = html.replace(/<title>[^<]*<\/title>/i, `<title>${esc(route.title)}</title>`)
  html = setMeta(html, 'name', 'description', desc)
  html = setCanonical(html, url)
  html = setMeta(html, 'property', 'og:title', route.title)
  html = setMeta(html, 'property', 'og:description', desc)
  html = setMeta(html, 'property', 'og:url', url)
  html = setMeta(html, 'property', 'og:image', ogImage)
  html = setMeta(html, 'property', 'og:type', 'website')
  html = setMeta(html, 'property', 'og:site_name', 'Wattzicht')
  html = setMeta(html, 'name', 'twitter:card', 'summary_large_image')
  html = setMeta(html, 'name', 'twitter:title', route.title)
  html = setMeta(html, 'name', 'twitter:description', desc)
  html = setMeta(html, 'name', 'twitter:image', ogImage)

  // Eerst alle JSON-LD uit de template halen, daarna die van deze route erin: zo krijgt elke pagina
  // alleen zijn eigen blokken en niet die van de homepage erbij.
  html = html.replace(/\s*<script type="application\/ld\+json">[\s\S]*?<\/script>/gi, '')
  const blocks = (route.jsonLd || [])
    .map((b) => `    <script type="application/ld+json">\n${JSON.stringify(b, null, 2)}\n    </script>`)
    .join('\n')
  if (blocks) html = html.replace('</head>', `${blocks}\n  </head>`)

  return html
}

let count = 0
for (const route of seo.routes) {
  const html = render(route)
  // "/" -> dist/index.html ; "/installateurs" -> dist/installateurs/index.html
  const outDir = route.path === '/' ? dist : join(dist, route.path)
  mkdirSync(outDir, { recursive: true })
  writeFileSync(join(outDir, 'index.html'), html, 'utf8')
  count++
  console.log(`  prerendered ${route.path}  ->  ${route.title}`)
}
console.log(`\nPrerender: ${count} statische HTML-bestanden geschreven.`)

// ---- sitemap.xml en robots.txt, uit dezelfde lijst ----
//
// Bewust geen lastmod: die zou bij elke build op vandaag komen te staan, en dan vertel je Google elke
// dag dat je hele site is veranderd. Dat is niet waar en het helpt niet.
const priority = (path) => {
  if (path === '/') return '1.0'
  if (path === '/privacy') return '0.3'
  return '0.8'
}

const urls = seo.routes
  .map((r) => `  <url>\n    <loc>${seo.baseUrl}${r.path}</loc>\n    <priority>${priority(r.path)}</priority>\n  </url>`)
  .join('\n')

writeFileSync(
  join(dist, 'sitemap.xml'),
  `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls}\n</urlset>\n`,
  'utf8',
)

writeFileSync(
  join(dist, 'robots.txt'),
  `User-agent: *\nAllow: /\n\nSitemap: ${seo.baseUrl}/sitemap.xml\n`,
  'utf8',
)

console.log(`Sitemap en robots.txt geschreven (${seo.routes.length} URL's).`)
