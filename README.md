# USGS Geomagnetic Activity Analysis

## Project Overview

This project collects and analyzes geomagnetic field measurements from three USGS observatories located at different latitudes:

- Barrow (BRW), Alaska
- Boulder (BOU), Colorado
- San Juan (SJG), Puerto Rico

The objective is to compare short-term changes in Earth's magnetic field across these locations and investigate how geomagnetic activity differs by latitude.

## Technologies Used

- **C# / .NET:** Console application for retrieving and processing data.
- **USGS Geomagnetism API:** Source of geomagnetic measurements.
- **JSON:** Format used for API responses.
- **SQLite:** Database for storing collected measurements.
- **Ubuntu Linux:** Operating system hosting the application.
- **DigitalOcean:** Cloud infrastructure.
- **systemd:** Automates hourly data collection.
- **SQL:** Queries used to analyze the collected measurements.

## Implementation

The application:

1. Calculates the most recently completed UTC hour.
2. Requests measurements from three USGS observatories.
3. Retrieves X, Y, Z, and F measurements at one-minute intervals.
4. Converts JSON responses into C# objects.
5. Validates station identifiers and measurement arrays.
6. Stores measurements in SQLite using transactions.
7. Prevents duplicate records using a composite primary key.
8. Preserves missing measurements as NULL values.

A systemd timer runs the application automatically every hour.

## Database Structure

The SQLite database contains a table named `GeomagReadings`.

| Column | Description |
|---|---|
| StationCode | Observatory identifier |
| TimestampUtc | UTC measurement timestamp |
| DataType | USGS data classification |
| X | Magnetic field X component |
| Y | Magnetic field Y component |
| Z | Magnetic field Z component |
| F | Total magnetic field intensity |

The primary key consists of StationCode, TimestampUtc, and DataType.

## Collected Data

As of October 25, 2026, the database contained 113,049 records across the three observatories, covering September 11 through October 23, 2026.

The `data` directory contains a CSV sample of measurements collected on October 18, 2026.

**Sample:** `data/geomagnetic_sample_2026-10-18.csv`

The sample includes 4,320 timestamped records. Some measurements contain missing values because the USGS API does not always provide complete data.

The live SQLite database is excluded from the repository.

## Running the Application

The application requires the .NET SDK and access to the USGS Geomagnetism API.

From the project directory:

`dotnet restore`

`dotnet run`

The application creates or opens `geomag.db` in its working directory.

The hourly systemd timer is configured separately on the Ubuntu server.

## Data Analysis

SQL queries are used to examine:

- Data completeness across observatories.
- Minute-to-minute magnetic field changes.
- Average and maximum changes at each observatory.
- Differences in geomagnetic activity across latitudes.

## Technical Challenges

Challenges encountered during development include:

- Handling incomplete API responses and missing measurements.
- Identifying and recovering gaps in historical data.
- Preventing duplicate database records.
- Maintaining automated hourly collection.
- Investigating HTTP 404 errors from the USGS endpoint beginning October 23, 2026.

## Data Source

U.S. Geological Survey (USGS), Geomagnetism Program.

https://www.usgs.gov/programs/geomagnetism
