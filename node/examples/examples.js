'use strict';

const { BuiltWithClient } = require('../src/index');

// Replace with your actual API key
var API_KEY = process.env.BUILTWITH_API_KEY || 'YOUR_API_KEY';

async function main() {
  const client = new BuiltWithClient(API_KEY);

  // ── Example 1: domain_lookup_live ────────────────────────────────────────
  console.log('--- domain_lookup_live ---');
  const live = await client.domain_lookup_live({ domain: 'spotify.com' });
  if (live.ok) {
    console.log('Technologies:', JSON.stringify(live.data, null, 2).substring(0, 500));
  } else {
    console.log('Error:', live.error);
  }

  // ── Example 2: domain_lookup ─────────────────────────────────────────────
  console.log('\n--- domain_lookup ---');
  const lookup = await client.domain_lookup({ lookup: 'spotify.com' });
  if (lookup.ok) {
    console.log('Domain data:', JSON.stringify(lookup.data, null, 2).substring(0, 500));
  } else {
    console.log('Error:', lookup.error);
  }

  // ── Example 3: change ───────────────────────────────────────────────────
  console.log('\n--- change ---');
  const changes = await client.change({ lookup: 'spotify.com', since: 'last month' });
  if (changes.ok) {
    console.log('Changes:', JSON.stringify(changes.data, null, 2).substring(0, 500));
  } else {
    console.log('Error:', changes.error);
  }

  // ── Example 4: trust ────────────────────────────────────────────────────
  console.log('\n--- trust ---');
  const trust_result = await client.trust({ lookup: 'spotify.com' });
  if (trust_result.ok) {
    console.log('Trust score:', JSON.stringify(trust_result.data, null, 2).substring(0, 500));
  } else {
    console.log('Error:', trust_result.error);
  }

  // ── Example 5: company_to_url ────────────────────────────────────────────
  console.log('\n--- company_to_url ---');
  const company = await client.company_to_url({ company: 'Spotify' });
  if (company.ok) {
    console.log('Company domains:', JSON.stringify(company.data, null, 2).substring(0, 500));
  } else {
    console.log('Error:', company.error);
  }
}

main().catch(console.error);
