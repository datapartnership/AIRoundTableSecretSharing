import { PublicClientApplication } from '@azure/msal-browser'

const BASE = `${(import.meta.env.VITE_REDIRECT_URL ?? '').replace(/\/$/, '')}/`

export const msalConfig = {
  auth: {
    clientId: '85124712-2ee2-4e87-b302-d82fb46aceda',
    authority: 'https://login.microsoftonline.com/fa60987f-99d7-464a-9994-9229f4e8c6e2',
    redirectUri: BASE,
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
  scopes: ['api://aiindex-api/access_as_user'],
}

export const msalInstance = new PublicClientApplication(msalConfig)
