# WHY: One digest-pinned manifest keeps fixture images, capability profiles, and local developer builds aligned.
# Fixtures read the FROM references as data; Compose builds the selected unchanged vendor-image stage.
FROM mysql:8.4.13@sha256:a81d043930e51745522bd5c430961cf8286f0a23f0a9a2a6e0108f7ea71b0902 AS mysql
FROM mariadb:11.8.9@sha256:e1d5495f445654134f226f55a41ce0da227116f6f27f283101f405c794d841fc AS mariadb
FROM postgres:17.12-alpine3.24@sha256:526f86d93c6ea08fd3f1fe5605650cacc781b2c6013169a0ad8f42c7d3d84f32 AS postgres
FROM mcr.microsoft.com/mssql/server:2022-CU28-ubuntu-22.04@sha256:4525e3efac9f3e241cb003bb2ff2a82818b502f37e12049d788b1a9fa7e13b7f AS sqlserver
