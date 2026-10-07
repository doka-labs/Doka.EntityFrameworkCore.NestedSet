# WHY: One digest-pinned manifest keeps fixture images, capability profiles, and local developer builds aligned.
# Fixtures read the FROM references as data; Compose builds the selected unchanged vendor-image stage.
FROM mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a AS mysql
FROM mariadb:11.8.9@sha256:6422478cb8e159f080fb1d8ccf65101e26fe51385787fde7d16c3b165a331f15 AS mariadb
FROM postgres:17.11-alpine3.24@sha256:b0f9560a2de083e2cc7382e75f808c7381a32852a7ec49117deedb300e552b24 AS postgres
FROM mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-24.04@sha256:2b5b581621126574f3d1f75e78d3eebe8d05aedb59ad0cfdf9aa42cb0634d726 AS sqlserver
