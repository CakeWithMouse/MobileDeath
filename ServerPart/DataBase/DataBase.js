var mariadb = require('mariadb');

let conn
let qRes

const qpool = mariadb.createPool({
    host: "127.0.0.1",
    port: 3310,
    user: "my_champ",
    password: "0Y4k9A8o",
    database: "champ_new",
    connectionLimit: 5
});

async function GetData(sql) {
    return await qpool.query(sql)
}
async function GetToken() {
    token = 0;
    return token;
}
module.exports = { GetData, GetToken }; 