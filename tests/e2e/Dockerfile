FROM mcr.microsoft.com/playwright:v1.62.0-noble@sha256:baed2032d533817f3dbe6425de795788430ba345e819a1201337009ba17c9d07

WORKDIR /work

COPY package.json package-lock.json ./
RUN npm ci

COPY playwright.config.js ./
COPY tests/e2e ./tests/e2e

ENTRYPOINT ["npm", "run", "test:e2e", "--"]
