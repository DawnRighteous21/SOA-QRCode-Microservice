# QR Code Generation & Scanning Microservice

**Course:** KTHDV (Service-Oriented Architecture) - Mid-Semester Project
**Group:** 7
**Group Code:** SOA.26-7

This repository contains a stateless C# Web API microservice dedicated to generating and decoding QR codes. The project architecture and implementation details are fully documented within the source codebase.

## 1. Business Value & Project Applicability
This microservice bridges the physical and digital worlds by allowing systems to encode data into scannable formats and extract data from physical images. Because it is designed as a stateless API, it does not require an internal database. It takes data in and passes an image out, or takes an image in and passes data out. 

This microservice is highly applicable to other university semester projects (e.g., Web Development, Mobile Programming, E-Commerce systems) that require physical verification without the overhead of building native QR processing logic from scratch.

## 2. Core Use Cases
*   **Product QR:** Retail and inventory systems can generate QR codes encoding product IDs, manufacturing dates, or serialized JSON payloads for immediate scanning in warehouses.
*   **Event Check-in:** Event management platforms can scan attendee QR codes at the door, extracting a unique registration hash to verify against a master database.
*   **Digital Tickets:** Booking systems can generate secure, scannable QR tickets for transportation, cinema, or concerts, embedding seat numbers and transaction IDs.

## 3. Technology Stack
The microservice is built for high compatibility and cross-platform execution:
*   **Framework:** ASP.NET Core Minimal API targeting `.NET 10.0`.
*   **QR Generation:** `QRCoder` (v1.8.0) is utilized to generate the raw QR matrices.
*   **QR Scanning:** `ZXing.Net` (v0.16.11) paired with `ZXing.Net.Bindings.ImageSharp.V2` (v0.16.20) handles cross-platform image decoding without relying on native Windows libraries (like `System.Drawing`).
*   **Documentation & Testing:** `Swashbuckle.AspNetCore` (v10.2.3) automatically generates a Swagger UI environment.

## 4. How to Run Locally
1. Ensure the .NET 10 SDK is installed on your machine.
2. Clone this repository and navigate to the `QRCodeApi` directory.
3. Run the following command in your terminal:
   ```bash
   dotnet run
   ```
4. By default, the application will launch and listen on `http://localhost:5077` (and `https://localhost:7251`).
5. To test the API endpoints interactively, open a browser and navigate to:
   ```
   http://localhost:5077/swagger
   ```

## 5. API Endpoints
The microservice exposes two primary endpoints registered directly on the `WebApplication` builder.

### `POST /generate`
Generates a QR code image from a given text payload.
*   **Input:** A plain JSON string payload provided in the request body (`[FromBody]`).
*   **Processing:** Generates a QR code utilizing Medium Error Correction (`ECCLevel.M`), which allows up to 15% of the code to be restored if damaged or dirty. 
*   **Output:** Returns a fully rendered `image/png` file constructed from a `PngByteQRCode` graphic array.

### `POST /scan`
Reads an uploaded QR code image and extracts the encoded text.
*   **Input:** An uploaded image file (`IFormFile`).
*   **Processing:** Loads the image stream asynchronously as an `Rgba32` pixel format and processes it through a stateless `BarcodeReader`. Antiforgery validation is explicitly disabled (`.DisableAntiforgery()`) on this endpoint to allow seamless requests from external microservices.
*   **Output:** Returns a JSON object containing the `decodedText`. 

## 6. Microservice Integration Strategy
To integrate this service into a larger system (such as a Spring Boot application or an ASP.NET Core monolith):
1.  **Deployment:** Host this API in a Docker container or a distinct local port (e.g., 5077).
2.  **Cross-Service Communication:** Whenever your main application needs to issue a digital ticket, it sends an HTTP POST request containing the ticket data to `http://<qr-service-ip>:5077/generate`.
3.  **Consumption:** Your main application receives the raw PNG byte array and can then save it to an S3 bucket, attach it to a Brevo email API workflow, or render it directly on a frontend application. 
4.  **Verification:** When a scanner application (like a mobile app) captures a QR image, it forwards the image file directly to the `/scan` endpoint, retrieves the parsed text, and passes that text back to your main database for authorization.
