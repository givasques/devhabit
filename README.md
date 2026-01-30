# DevHabit API

DevHabit is a RESTful Web API designed to help users manage habits, tags, and personal progress, with GitHub integration and HATEOAS-driven responses. 
The API follows modern best practices such as versioning, content negotiation, and structured error handling.

## Overview

DevHabit API allows authenticated users to:

- Manage their habits

- Organize habits with tags

- Access and update their user profile

- Integrate with GitHub to retrieve profile data

- Navigate resources using HATEOAS links

The API is built with a strong focus on clean architecture, REST principles, and scalable design.

## Tech Stack

- .NET / ASP.NET Core Web API

- RESTful architecture

- JWT-based Authentication

- HATEOAS (Hypermedia as the Engine of Application State)

- API Versioning

- Swagger / OpenAPI documentation

- Structured error handling using ProblemDetails

## Authentication

Most endpoints require authentication.

After login, the API returns access tokens, which must be sent in subsequent requests via the Authorization header:

- Authorization: Bearer {your_token}


Successful authentication returns an AccessTokensDto containing the issued tokens. 


## Content Negotiation & Versioning

The API supports multiple media types and versions through the Accept header, including:

- application/json

- application/json;v=1

- application/json;v=2

Custom HATEOAS media types like
- application/vnd.dev-habit.hateoas+json

This controls both API version and whether HATEOAS links are included in responses. 

## HATEOAS

Many responses include a links collection, allowing clients to dynamically discover available actions. This makes the API more self-descriptive and navigable. 

## Error Handling

The API uses standard HTTP status codes and returns structured error responses using ProblemDetails, including:

- 400 – Bad Request

- 401 – Unauthorized

- 403 – Forbidden

- 404 – Not Found

- 409 – Conflict

All errors follow a consistent schema. 

## Documentation
- Available in: https://dev-habit-api-aeajgqecgnafbehu.spaincentral-01.azurewebsites.net/scalar/
