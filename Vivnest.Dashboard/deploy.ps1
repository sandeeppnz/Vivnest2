TOKEN=$(az staticwebapp secrets list --name vivnest-dashboard --resource-group rg-vivnest-dev --query "properties.apiKey" -o tsv) 

npx --yes @azure/static-web-apps-cli deploy ./dist --deployment-token "$TOKEN" --env production 2>&1