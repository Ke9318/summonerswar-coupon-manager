# Summoners War Coupon Manager

Tampermonkey userscript for Summoners War coupon management.

## Features
- SWGT Active Codes scan
- NEW coupon detection
- GUI account add/edit/delete
- Per-account coupon history
- Run only new coupons or all active coupons
- Background worker tab for coupon registration
- Worker tab closes after completion
- Tampermonkey auto-update via GitHub

## Install
Install `SW_Coupon_Manager.user.js` in Tampermonkey.

## Auto update
Tampermonkey uses the script's `@updateURL` and `@downloadURL` metadata.
Publish a newer `@version` to this repository to distribute an update.

## v4.7
- Fixed `새 쿠폰만`: it now runs active coupons that have not been completed for each selected account, so a pre-run rescan no longer clears the work queue.
