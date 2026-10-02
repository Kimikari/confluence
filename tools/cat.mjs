#!/usr/bin/env node
// Selective queries over docs/confluence/confluence-catalogue.json (never dump the whole file).
//   node tools/cat.mjs stats
//   node tools/cat.mjs get <ID> [ID...]              full records (any type)
//   node tools/cat.mjs kit <WIZ-ID>                   wizard + its 8 spells + its 16 transformations (compact)
//   node tools/cat.mjs list <type> [field=value ...] [--fields a,b,c] [--limit N]
//        type: wizards|spells|equipment|augments|biomes|modules|props|builds
//        filters: field=value (exact, arrays match membership), field~regex, unconditional, transform, shared
//   node tools/cat.mjs grep <regex> [type] [--limit N]  records whose JSON matches
//   node tools/cat.mjs refs <ID>                       records that reference ID
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const cat = JSON.parse(fs.readFileSync(path.join(root, 'docs', 'confluence', 'confluence-catalogue.json'), 'utf8'));
const C = cat.content;
const all = Object.entries(C).flatMap(([type, arr]) => arr.map((r) => ({ type, r })));
const byId = new Map(all.map((x) => [x.r.id, x]));

const argv = process.argv.slice(2);
const opt = (name, def) => { const i = argv.indexOf(name); if (i < 0) return def; const v = argv[i + 1]; argv.splice(i, 2); return v; };
const limit = +opt('--limit', 1000);
const fields = (opt('--fields', '') || '').split(',').filter(Boolean);
const [cmd, ...args] = argv;

const isTransform = (a) => (a.target_spell_ids || []).length > 0;
const isUnconditional = (a) => a.eligibility && Object.keys(a.eligibility).length === 1 && 'always' in a.eligibility;
const pick = (r) => (fields.length ? Object.fromEntries(fields.map((f) => [f, r[f]])) : r);
const print = (r) => console.log(JSON.stringify(pick(r)));

function matches(r, filt) {
  if (filt === 'transform') return isTransform(r);
  if (filt === 'shared') return !isTransform(r);
  if (filt === 'unconditional') return isUnconditional(r);
  let m = /^([\w.]+)~(.*)$/.exec(filt);
  if (m) return new RegExp(m[2], 'i').test(JSON.stringify(r[m[1]] ?? ''));
  m = /^([\w.]+)=(.*)$/.exec(filt);
  if (m) { const v = r[m[1]]; return Array.isArray(v) ? v.map(String).includes(m[2]) : String(v) === m[2]; }
  throw new Error('bad filter ' + filt);
}

switch (cmd) {
  case 'stats': {
    console.log('counts', JSON.stringify(cat.counts));
    console.log('rules', JSON.stringify(cat.rules));
    const cnt = (xs) => xs.reduce((m, x) => ((m[x] = (m[x] || 0) + 1), m), {});
    console.log('augment rarity', JSON.stringify(cnt(C.augments.map((a) => a.rarity))));
    console.log('shared unconditional by rarity', JSON.stringify(cnt(C.augments.filter((a) => !isTransform(a) && isUnconditional(a)).map((a) => a.rarity))));
    console.log('equipment tiers', JSON.stringify(cnt(C.equipment.map((e) => e.category + ' T' + e.tier))));
    break;
  }
  case 'get':
    for (const id of args) { const x = byId.get(id); if (!x) console.error('missing ' + id); else print(x.r); }
    break;
  case 'kit': {
    const w = C.wizards.find((x) => x.id === args[0]);
    if (!w) { console.error('missing'); process.exit(1); }
    print(w);
    for (const id of w.spell_ids) print(byId.get(id).r);
    for (const a of C.augments.filter((a) => isTransform(a) && (a.eligible_wizards || []).includes(w.id)))
      console.log(JSON.stringify({ id: a.id, name: a.name, rarity: a.rarity, target: a.target_spell_ids, group: a.exclusive_group, rank1: a.rank1, rank2: a.rank2, drawback: a.drawback, counterplay: a.counterplay, stacking_rule: a.stacking_rule, activation_rule: a.activation_rule, reaction_control: a.reaction_control, tags: a.tags }));
    break;
  }
  case 'list': {
    const [type, ...filters] = args;
    let n = 0;
    for (const r of C[type] || []) { if (filters.every((f) => matches(r, f))) { print(r); if (++n >= limit) break; } }
    console.error(`(${n} records)`);
    break;
  }
  case 'grep': {
    const re = new RegExp(args[0], 'i'); let n = 0;
    for (const x of all) { if (args[1] && x.type !== args[1]) continue; if (re.test(JSON.stringify(x.r))) { print(x.r); if (++n >= limit) break; } }
    console.error(`(${n} records)`);
    break;
  }
  case 'refs': {
    const id = args[0]; let n = 0;
    for (const x of all) if (x.r.id !== id && JSON.stringify(x.r).includes('"' + id + '"')) { console.log(x.type, x.r.id, x.r.name); if (++n >= limit) break; }
    break;
  }
  default:
    console.log('usage: node tools/cat.mjs stats|get|kit|list|grep|refs  (see header comment)');
}
