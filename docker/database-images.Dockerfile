# WHY: One digest-pinned manifest keeps fixture images, capability profiles, and local developer builds aligned.
# Fixtures read the FROM references as data; Compose builds the selected unchanged vendor-image stage.
FROM mysql:9.7.1@sha256:cbade841779f1661300e705721f9d2ff159865cc7a8a291affbff43ac6ec7f1d AS mysql
FROM mariadb:11.8.9@sha256:79d59758afc91b89b120b0a8904d637f5a3b3e1c4900f29b740d6d46c72fef68 AS mariadb
FROM postgres:17.11-alpine3.24@sha256:f02121de6f74d30d8a94cd1d9584125e2178d7e6c377d8130112d4e52d867995 AS postgres
FROM mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-24.04@sha256:2b5b581621126574f3d1f75e78d3eebe8d05aedb59ad0cfdf9aa42cb0634d726 AS sqlserver
