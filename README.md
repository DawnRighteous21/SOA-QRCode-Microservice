# SOA QR Code Generation & Verification Microservice

**Course:** KTHDV (Service-Oriented Architecture) - Mid-Semester Project  
**Group:** 7  
**Group Code:** SOA.26-7  

This repository contains a hybrid C# Minimal API microservice dedicated to generating and verifying QR codes. It is designed to act as an independent node in a Service-Oriented Architecture (SOA), completely decoupling physical-to-digital verification logic from core business applications.

## 1. Business Value (What problem does it solve?)
This microservice acts as a centralized engine for QR rendering and cryptographic validation. 
* **Decoupling:** Frontend and Backend teams do not need to implement complex QR generation, rate-limiting, or cryptographic signature algorithms in their core codebases.
* **Interoperability:** Any system (e.g., Spring Boot, Python Flask, React, Mobile Apps) can integrate with it seamlessly via standard RESTful HTTP requests and JSON payloads.
* **Centralized Security:** Prevents ticket forgery, Sybil attacks, and duplicate check-ins at the architectural level rather than the application level.

## 2. Core Architectural Use Cases
This service demonstrates three distinct architectural routing patterns:
1. **Event Check-in (Stateful Broadcast):** A single dynamic QR code with a Hard Expiration Timer (TTL). Mobile scanners hit the verify endpoint to record attendance based on hardware device fingerprinting.
2. **Digital Tickets (Stateful Single-Use):** Issues secure, HMAC-SHA256 signed individual tickets. Scanned tickets are tracked in a concurrent dictionary and "burned" upon use to explicitly prevent passback attacks.
3. **Product QR (Stateless Multi-Use):** Generates unlimited canonical routing tags for physical assets, decoupling physical inventory items from backend databases.

## 3. Enterprise Security Implementations
This microservice goes beyond basic QR generation by implementing a production-grade security suite:
* **DDoS Protection:** Global `FixedWindowLimiter` middleware drops requests exceeding 10 hits per second to prevent endpoint flooding.
* **Cryptographic Signatures:** Ticket payloads are signed using `HMAC-SHA256` utilizing a private server secret. Any tampering with the payload instantly triggers a Forgery Alert.
* **Device Fingerprinting:** The verification node extracts client IPs via proxy headers (`X-Forwarded-For`) combined with raw `User-Agent` strings to map physical devices to attendance logs, preventing remote "Absent Friend" scanning exploits.
* **Hard Session Expiration (TTL):** Embedded Unix timestamps automatically invalidate sessions, paired with dynamic UI blurring on the dashboard to prevent user confusion.

## 4. Technology Stack
* **Framework:** ASP.NET Core Minimal API targeting `.NET 10.0`.
* **QR Processing:** `QRCoder` (v1.8.0) for generation and `ZXing.Net` for cross-platform image decoding.
* **Documentation & Integration:** `Swashbuckle.AspNetCore` (v6.6.2) for automated OpenAPI/Swagger UI generation.

## 5. How to Run Locally & Test
1. Ensure the **.NET 10 SDK** is installed.
2. Clone this repository and navigate to the project directory.
3. Run the application:
   ```bash
   dotnet run
   ```
4. The service will listen on port `5077`.
5. Access the **SOA Verification Dashboard** at: `http://localhost:5077`
6. Access the **Swagger UI** for interactive API documentation at: `http://localhost:5077/swagger`

## 6. Live Demonstration Setup (Ngrok Configuration)
Since QR codes must be scanned by external mobile devices during the seminar, the local localhost server must be exposed to the public internet securely.

1. Download and install [ngrok](https://ngrok.com/download).
2. Create a free account at [dashboard.ngrok.com](https://dashboard.ngrok.com).
3. Navigate to **Your Authtoken** in the ngrok dashboard, copy the token, and bind it to your local machine by running this command in your terminal:
   ```bash
   ngrok config add-authtoken <YOUR_COPIED_TOKEN>
   ```
4. While the `.NET` server is running on port `5077`, open a new terminal and start the HTTP tunnel:
   ```bash
   ngrok http 5077
   ```
5. Copy the generated `Forwarding` URL (e.g., `https://xxxx.ngrok-free.app`).
6. Open the SOA Verification Dashboard (`http://localhost:5077`) in your browser.
7. Paste the ngrok URL into the **PUBLIC GATEWAY** field at the top right of the screen. All subsequently generated QR codes will now contain public internet links accessible by any smartphone in the room.

## 7. Core API Endpoints
*For interactive testing and schema details, please navigate to the `/swagger` endpoint while the server is running.*

* `GET /api/checkin/create` - Generates a stateful broadcast Event Check-In QR code.
* `GET /api/ticket/create` - Issues a secure HMAC-SHA256 signed digital ticket.
* `GET /api/product/create` - Generates a stateless multi-use product tag.
* `GET /checkin/verify/{id}` - Client verification endpoint for Event Check-In.
* `GET /ticket/verify` - Client verification endpoint for Digital Tickets.

## 8. Microservice Integration Strategy
To integrate this QR service into a larger ecosystem (such as a Spring Boot e-commerce backend or a Python ticket booking system):

1. **Cross-Service Call:** When the upstream application needs to issue a ticket, it initiates an HTTP GET request to our endpoint: `http://<qr-service-ip>:5077/api/ticket/create?attendee=JohnDoe&tier=VIP`.
2. **Consumption:** The main application receives the JSON response containing the `ticketId`, `signature`, and raw `qrBase64` image string.
3. **Rendering:** The main application saves the `ticketId` to its internal database, then injects the `qrBase64` string directly into an HTML `<img>` tag or an email template for the end-user.
4. **Verification:** When a user scans the printed QR code with their mobile device, the request is routed directly to the Microservice's Verification Node, bypassing the main booking system entirely to validate the cryptographic signature and manage rate-limiting.
