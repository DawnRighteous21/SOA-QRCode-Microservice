# SOA QR Code Generation & Verification Microservice

**Course:** KTHDV (Service-Oriented Architecture) - Mid-Semester Project
**Group:** 7
**Group Code:** SOA.26-7

This repository contains a stateless/stateful C# Minimal API microservice dedicated to generating and verifying QR codes. It is designed to act as an independent node in a Service-Oriented Architecture (SOA), decoupling physical-to-digital verification logic from core business applications.

## 1. Business Value (What problem does it solve?)
This microservice acts as a centralized engine for QR rendering and cryptographic validation. 
* **Decoupling:** Frontend and Backend teams do not need to implement complex QR generation or cryptographic signature (HMAC-SHA256) algorithms in their core codebases.
* **Interoperability:** Any system (e.g., Spring Boot, Python Flask, Node.js) can integrate with it seamlessly via standard HTTP requests and JSON payloads.
* **Centralized Security:** Prevents ticket forgery and duplicate check-ins at the architectural level rather than the application level.

## 2. Core Use Cases & Applicability
This service is highly applicable to other university semester projects (Web Development, Mobile Programming, E-Commerce) that require physical verification:
* **Event Check-in (Stateful / TTL):** Broadcasts a QR code with a Time-To-Live. Mobile scanners hit the verify endpoint to record attendance based on device fingerprinting.
* **Digital Tickets (Stateful / Single-Use):** Issues secure, HMAC-SHA256 signed tickets for booking systems. Scanned tickets are tracked in a concurrent dictionary and burned upon use to prevent passback attacks.
* **Product QR (Stateless / Multi-use):** Generates canonical routing tags for physical products, decoupling inventory systems from the scanning interfaces.

## 3. Technology Stack
* **Framework:** ASP.NET Core Minimal API targeting `.NET 10.0`.
* **QR Processing:** `QRCoder` (v1.8.0) for generation and `ZXing.Net` for cross-platform image decoding.
* **Documentation:** `Swashbuckle.AspNetCore` for automated OpenAPI/Swagger UI generation.

## 4. How to Run Locally & Test
1. Ensure the .NET 10 SDK is installed.
2. Clone this repository and navigate to the project directory.
3. Run the application:
   ```bash
   dotnet run

```

4. The service will listen on port `5077`.
5. Access the **SOA Verification Dashboard** at: `http://localhost:5077`
6. Access the **Swagger UI** for interactive API documentation at: `http://localhost:5077/swagger`

## 5. Core API Endpoints

*Note: All generation endpoints return a JSON object containing the `qrBase64` image string alongside necessary metadata.*

* `GET /api/checkin/create`
Generates a stateful broadcast Event Check-In QR code with a customizable expiration timer.
* `GET /api/ticket/create`
Issues a secure HMAC-SHA256 signed digital ticket. Accepts `attendee` and `tier` query parameters.
* `GET /api/product/create`
Generates a stateless multi-use product tag linked to a specific physical asset.

## 6. Microservice Integration Strategy

To integrate this QR service into a larger ecosystem (such as a Spring Boot e-commerce backend or a Python ticket booking system):

1. **Cross-Service Call:** When your main application needs to issue a ticket, it initiates an HTTP GET request to `http://<qr-service-ip>:5077/api/ticket/create?attendee=JohnDoe&tier=VIP`.
2. **Consumption:** Your main application receives the JSON response containing the `ticketId`, `signature`, and raw `qrBase64` image string.
3. **Rendering:** The main application saves the `ticketId` to its internal database, then injects the `qrBase64` string directly into an HTML `<img>` tag or an email template for the end-user.
4. **Verification:** When a user scans the printed QR code with their mobile device, the request is routed directly to the Microservice's Verification Node, bypassing the main booking system entirely to validate the cryptographic signature.

```

