// Sets up the Mummy Rush Discord server: roles, categories, channels and their permissions.
// Run it yourself, with your bot's token in the environment (never commit or share the token):
//
//   PowerShell:  $env:DISCORD_TOKEN = "<token>"; node tools/discord-setup/setup.mjs <server id>
//   Git Bash:    DISCORD_TOKEN=<token> node tools/discord-setup/setup.mjs <server id>
//
// Safe to run again: roles and channels that already exist (same name) are left as they are.
// The bot needs the "Manage Roles" and "Manage Channels" permissions on the server.

const token = process.env.DISCORD_TOKEN;
const guildId = process.argv[2];
if (!token || !guildId) {
  console.error("Usage: DISCORD_TOKEN=<token> node tools/discord-setup/setup.mjs <server id>");
  process.exit(1);
}

const API = "https://discord.com/api/v10";
const VIEW = 1n << 10n, SEND = 1n << 11n, CONNECT = 1n << 20n, MANAGE_CHANNELS = 1n << 4n;
const TEXT = 0, VOICE = 2, CATEGORY = 4;

async function call(method, path, body) {
  for (;;) {
    const res = await fetch(API + path, {
      method,
      headers: { Authorization: `Bot ${token}`, "Content-Type": "application/json" },
      body: body ? JSON.stringify(body) : undefined,
    });
    if (res.status === 429) { // rate limited: wait as told, then again
      const wait = (await res.json()).retry_after ?? 1;
      await new Promise(r => setTimeout(r, wait * 1000 + 100));
      continue;
    }
    if (!res.ok) throw new Error(`${method} ${path}: ${res.status} ${await res.text()}`);
    return res.status === 204 ? null : res.json();
  }
}

// ---- Roles, from the top of the list down.
const ROLES = [
  { name: "Développeur", color: 0xE8B84A, hoist: true },
  { name: "Modérateur", color: 0x40E0D0, hoist: true },
  { name: "Testeur", color: 0x9B6BD6, hoist: true },
  { name: "Chef de guilde", color: 0xD67A40, hoist: false },
  { name: "Joueur", color: 0x9C8B70, hoist: false },
];

// ---- Categories and channels. readOnly: everyone reads, only the team writes. only: visible to these roles alone.
const LAYOUT = [
  {
    category: "📜 Infos", readOnly: true, channels: [
      { name: "bienvenue", topic: "Bienvenue dans les tombeaux de Mummy Rush !" },
      { name: "règles", topic: "Le règlement du serveur." },
      { name: "annonces", topic: "Les nouvelles du jeu." },
      { name: "notes-de-patch", topic: "Ce qui change à chaque version." },
    ],
  },
  {
    category: "💬 Communauté", channels: [
      { name: "discussion", topic: "Tout ce qui touche à Mummy Rush." },
      { name: "recherche-duo", topic: "Trouve un partenaire pour le 2v2 (pense à donner ton code ami Nom#1234)." },
      { name: "guildes", topic: "Présente ta guilde, recrute, cherche une guilde." },
      { name: "replays", topic: "Partage tes meilleurs duels et tes plus belles évasions." },
      { name: "hors-sujet", topic: "Pour parler d'autre chose." },
    ],
  },
  {
    category: "🛠️ Aide", channels: [
      { name: "questions", topic: "Une question sur le jeu ? Demande ici." },
      { name: "bugs", topic: "Un bug ? Décris ce que tu faisais, ce qui s'est passé, ta version du jeu et ton téléphone." },
      { name: "suggestions", topic: "Tes idées pour le jeu." },
    ],
  },
  {
    category: "🧪 Testeurs", only: ["Testeur", "Développeur", "Modérateur"], channels: [
      { name: "tests-internes", topic: "Retours sur les versions de test." },
    ],
  },
  {
    category: "🔒 Équipe", only: ["Développeur", "Modérateur"], channels: [
      { name: "modération", topic: "Entre modérateurs." },
    ],
  },
  {
    category: "🔊 Vocal", channels: [
      { name: "Duo 1", type: VOICE },
      { name: "Duo 2", type: VOICE },
      { name: "Guilde", type: VOICE },
    ],
  },
];

const roles = await call("GET", `/guilds/${guildId}/roles`);
const roleId = {};
for (const r of roles) roleId[r.name] = r.id;
for (const r of ROLES) {
  if (roleId[r.name]) { console.log(`role "${r.name}": already there`); continue; }
  const made = await call("POST", `/guilds/${guildId}/roles`, { name: r.name, color: r.color, hoist: r.hoist, mentionable: true });
  roleId[r.name] = made.id;
  console.log(`role "${r.name}": created`);
}

// The bot itself must see the private categories to create channels in them.
const botId = (await call("GET", "/users/@me")).id;
const botAccess = { id: botId, type: 1, allow: String(VIEW | SEND | CONNECT | MANAGE_CHANNELS), deny: "0" };

const team = ["Développeur", "Modérateur"];
function overwrites(section) {
  const everyone = guildId; // the @everyone role has the server's id
  const list = [];
  if (section.only) {
    list.push({ id: everyone, type: 0, allow: "0", deny: String(VIEW) });
    for (const name of section.only) list.push({ id: roleId[name], type: 0, allow: String(VIEW | SEND | CONNECT), deny: "0" });
    list.push(botAccess);
  } else if (section.readOnly) {
    list.push({ id: everyone, type: 0, allow: String(VIEW), deny: String(SEND) });
    for (const name of team) list.push({ id: roleId[name], type: 0, allow: String(SEND), deny: "0" });
  }
  return list;
}

const channels = await call("GET", `/guilds/${guildId}/channels`);
const find = (name, type, parent) => channels.find(c => c.name === name && c.type === type && (parent === undefined || c.parent_id === parent));
for (const section of LAYOUT) {
  let cat = find(section.category, CATEGORY);
  if (cat && section.only) {
    // A private category made before the bot gave itself access (first runs of this script).
    await call("PUT", `/channels/${cat.id}/permissions/${botId}`, { type: 1, allow: botAccess.allow, deny: "0" });
  }
  if (cat) console.log(`category "${section.category}": already there`);
  else {
    cat = await call("POST", `/guilds/${guildId}/channels`, { name: section.category, type: CATEGORY, permission_overwrites: overwrites(section) });
    channels.push(cat);
    console.log(`category "${section.category}": created`);
  }
  for (const ch of section.channels) {
    const type = ch.type ?? TEXT;
    if (find(ch.name, type, cat.id)) { console.log(`  #${ch.name}: already there`); continue; }
    // Channels follow their category's permissions (same overwrites).
    const made = await call("POST", `/guilds/${guildId}/channels`, {
      name: ch.name, type, parent_id: cat.id, topic: ch.topic, permission_overwrites: overwrites(section),
    });
    channels.push(made);
    console.log(`  #${ch.name}: created`);
  }
}
console.log("Done. Give yourself the Développeur role in the server settings.");
