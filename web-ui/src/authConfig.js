import { PublicClientApplication } from '@azure/msal-browser'

const BASE = `${(import.meta.env.VITE_REDIRECT_URL ?? '').replace(/\/$/, '')}/`

export const msalConfig = {
  auth: {
    clientId: import.meta.env.VITE_MSAL_CLIENT_ID,
    authority: import.meta.env.VITE_MSAL_AUTHORITY,
    redirectUri: BASE,
    knownAuthorities: ["localhost:5100"], // Required for local/custom identity providers
  },
  cache: {
    cacheLocation: 'sessionStorage',
    storeAuthStateInCookie: false,
  },
}

export const loginRequest = {
  scopes: ['openid', 'profile', 'email'],
}

export const apiTokenRequest = {
  scopes: [import.meta.env.VITE_MSAL_API_SCOPE],
}

export const msalInstance = new PublicClientApplication(msalConfig)
