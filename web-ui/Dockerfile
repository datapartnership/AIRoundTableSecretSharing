FROM node:20-alpine

WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --registry=https://registry.npmjs.org/ \
    && test -x node_modules/.bin/vite

COPY . .

EXPOSE 3000

CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0"]
