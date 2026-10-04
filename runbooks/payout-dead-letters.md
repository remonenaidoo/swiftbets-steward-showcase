---
id: payout-dead-letters
title: Payout dead letters (winners parked and unpaid)
---
# Payout dead letters

## Symptoms
- `PayoutDeadLetters` alert: `swiftbets_payout_dead_letters` above zero for five minutes.
- Punter-facing: a bet shows `won` in bet history but the winnings never reached the wallet.

## Evidence to collect
- The parked payouts and their reasons: `GET /dead-letters` on payout (the reason is usually `retry ladder exhausted: wallet unavailable`).
- Whether the wallet is healthy now (its health endpoint and gRPC errors in payout's logs).
- When they were parked: a burst at one time points to an outage, a steady trickle to a payout or wallet defect.

## Diagnosis
- **Parked during a wallet outage, wallet healthy now:** the ladder (5 s, 1 m, 15 m) ran out before the wallet came back. Safe to redrive.
- **Wallet still failing:** redriving now only parks them again. Treat it as a wallet outage first (`wallet-outage`).
- **Blacklisted wallet or another refusal:** each needs a person's decision; do not redrive in bulk.

## Remediation
- Once the cause is fixed, propose `replay_dead_letter` per coupon and version, or call `POST /dead-letters/redrive?limit=500` on payout until it reports 0 redriven.
- Both are idempotent: payout pays only the difference between the settlement's target and what was already paid, under versioned idempotency keys, so a replay can never pay twice.

## Verification
- `swiftbets_payout_dead_letters` is 0.
- For each redriven coupon, payout's `PaidToDate` equals the settlement payout for its latest version, and the wallet has exactly one credit per idempotency key.
