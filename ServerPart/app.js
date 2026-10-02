var createError = require('http-errors');
var express = require('express');
var fileUpload = require('express-fileupload')
var path = require('path');
var cookieParser = require('cookie-parser');
var logger = require('morgan');

var bodyParser = require('body-parser');
var cors = require('cors');
var usersRouter = require('./routes/users');
var SQL_req = require('./DataBase/InfoSql');
var SQL_post = require('./DataBase/PostSql');
var SQL_check = require('./DataBase/CheckSql');

var app = express(),
    bodyParser = require('body-parser'),
    session = require('express-session');

app.use(cors())
app.disable('etag');
const port = 3005;

app.use(cookieParser())
app.use(
    session({
        secret: 'Mysarios',
        saveUninitialized: true,
        resave: true,
        maxAge: 60*60*24*10 * 1000
    })
)

app.get('/login', async (req, res) => {
    User = await SQL_check.login_in(req.query);
    if (User.ID) {
        Token = await SQL_check.CheckTokenByID(User)
        if (Token.length != 0) {
            res.cookie("token", Token[0]["Token"],{ maxAge: 60*60*24*10 * 1000})
            res.cookie.maxAge
        } else {
            await SQL_check.StartSession(req.sessionID, User["ID"])
            token = req.sessionID
            res.cookie("token", token,{ maxAge: 60*60*24*10 * 1000});
        }
    }
    res.send(User);
});

app.use(bodyParser.json()); // read Json from client



app.get('/',async (req, res) => {
    res.sendFile(__dirname + "/public/Htmls/index.html");
})
app.use(express.static("public")); // Open public to client

app.use(async function (req, res, next) { // Read body and check access
    if (req.cookies.token === undefined) {
        res.redirect('/');
    } else {
        ID = await SQL_check.CheckTokenByToken(req.cookies.token);
        if (ID.length === 0 ) {
            res.clearCookie("token")
            res.redirect('/Htmls/index.html');
        } else {
            req.query["ID"] = ID[0]["UserID"];
            next();
        }
    }
});
app.use(async function (req, res, next) { // Read body and check access
    next()
});
app.use(express.static("client")); // Open public to client

//Gets
app.get('/up', (req, res) => {  //0
    res.send()
    res.redirect("/Htmls/main.html");
});
app.get('/del', async (req, res) => { //0
    await SQL_req.ClearToken(req.query)
    res.clearCookie("token")
    res.redirect("/Htmls/index.html");
});

app.use(async function (req, res, next) { // Read body and check access
    Role = await SQL_req.GetRole(req.query);
    if(Role === "Error"){
        res.clearCookie("token")
        res.redirect("/Htmls/index.html")
    }else{
        req.query["UserRole"] = Role[0]["Role"]
        next();
    }
    
});

app.get('/info/GetUser/', async (req, res) => { //0
    result = await SQL_req.GetUserInfo(req.query);
    res.send(result);
});

app.get('/info/GetChamps', async (req, res) => { //0
    Champs = await SQL_req.GetChampsInfo(req.query);
    res.send(Champs);
})

app.get('/info/GetTeamsByChamp/', async (req, res) => { //0
    Teams = await SQL_req.GetTeamsInfo(req.query);
    res.send(Teams);
});
app.get('/info/GetTeam/', async (req, res) => { //0
    Team = await SQL_req.GetTeamInfo(req.query)
    res.send(Team);
});
app.get('/info/GetCriterias/', async (req, res) => {  //0
    Criterias = await SQL_req.GetCriteriasInfo(req.query);
    res.send(Criterias);
});
app.get('/info/AllCriteriasByChamp', async (req, res) => {  //0
    Criterias = await SQL_req.GetAllCriteriasInfo(req.query);
    res.send(Criterias);
});
app.get('/info/GetCompetenseByChampionateCompetence', async (req, res) => {  //0
    result = await SQL_req.GetCompetenseByChampAndComp(req.query);
    res.send(result);
});

app.get('/info/GetCompetence', async (req, res) => { //0
    result = await SQL_req.GetCompetence();
    res.send(result);
});
app.get('/info/GetMarks/', async (req, res) => { //0
    result = await SQL_req.GetMarksByUserAndTeam(req.query);
    res.send(result);
})
app.get('/info/UserByChampWithStatus', async (req, res) => { //0
    result = await SQL_req.GetUserByChampWithStatus(req.query);
    res.send(result);
})
app.get('/Users/SendStatus', async (req, res) => { //0
    result = await SQL_post.SendStatus(req.query);
    res.send(result);
});
app.post('/Criterias/AddValueByUser', (req, res) => { //0
    SQL_post.SaveMark(req.body);
    res.send({ "Status": "Success" })
});
app.post('/Criterias/AddCommentByUser', (req, res) => { //0
    SQL_post.SaveComment(req.body);
    res.send({ "Status": "Success" })
});
app.get('/Add/TeamToChamp', async (req, res) => { //0
    result = await SQL_req.AddTeamToChamp(req.query);
    res.send(result);
});
app.get('/Add/UserToChamp', async (req, res) => { //0
    result = await SQL_req.AddUserToChamp(req.query);
    res.send(result);
});
app.get('/Delete/UserFromChamp', async (req, res) => { //0
    result = await SQL_req.DeleteUserFromChamp(req.query);
    res.send(result);
});
app.use(async function (req, res, next) { // Read body and check access
    if (req.query["UserRole"] === 0) {
        res.sendStatus(403);
    } else {
        next();
    }
});
app.use(express.static("admin")); // Open public to admin

//Search
app.get('/Search/FindUser', async (req, res) => { //1
    result = await SQL_req.SearchUserByWord(req.query);
    res.send(result);
});
app.get('/Search/FindTeam', async (req, res) => { //1
    result = await SQL_req.SearchTeamByWord(req.query);
    res.send(result);
});
app.get('/Search/FindChamp', async (req, res) => { //1
    result = await SQL_req.SearchChampByWord(req.query);
    res.send(result);
});
app.get('/Repair/Marks', async (req, res) => { //1
    result = await SQL_req.RepairMarks(req.query);
    res.send(result);
});
app.get('/Repair/Marks_copu', async (req, res) => { //1
    result = await SQL_req.RepairMarks_copy(req.query);
    res.send(result);
});

//Create
app.get('/Create/Championate', async (req, res) => { //0
    result = await SQL_req.CreateChampoinate(req.query);
    res.send(result);
});
app.get('/Create/Team', async (req, res) => { //0
    result = await SQL_req.CreateTeam(req.query);
    res.send(result);
});
app.get('/Create/Organization', async (req, res) => { //0
    result = await SQL_req.CreateOrganization(req.query);
    res.send(result);
});
app.post('/Criteria/Create', async (req, res) => { //1
    result = await SQL_post.CreateCriteria(req.body);
    res.send(result);
})

app.get('/Criteria/Value/Delete', async (req, res) => { //1
    result = await SQL_post.DeleteValue(req.query);
    res.send(result);
});
app.get('/Criteria/Delete', async (req, res) => { //1
    result = await SQL_post.DeleteCriteria(req.query);
    res.send(result);
});
app.get('/Users/Delete', async (req, res) => { //1
    result = await SQL_req.DeleteUser(req.query);
    res.send(result);
});
app.get('/Team/Delete', async (req, res) => { //1
    result = await SQL_req.DeleteTeamByChampionate(req.query);
    res.send(result);
});

app.get('/User/GetResultsByChampInTxt', async (req, res) => { //1
    resultText = await SQL_req.GetResultsFromText(req.query);
    res.send(resultText);
})
app.get('/User/GetResultsByChampInExcell', async (req, res) => { //1
    resultText = await SQL_req.GetResultsFromExcell(req.query);
    res.send(resultText);
})

app.get('/Users/GetUsers', async (req, res) => { //1
    result = await SQL_req.GetUsers();
    res.send(result);
});
app.get('/info/AllTeams', async (req, res) => { //1
    result = await SQL_req.AllTeams();
    res.send(result);
});
app.get('/info/AllTeamsByChampionate', async (req, res) => { //1
    result = await SQL_req.AllTeamsByChampionate(req.query);
    res.send(result);
});

app.get('/User/OpenAccess', async (req, res) => { //1
    result = await SQL_req.OpenAccess(req.query);
    res.send({ "Status": "Success" })
});
app.get('/Championate/UpdateName', async (req, res) => { //1
    result = await SQL_req.ChampionateUpdateName(req.query);
    res.send({ "Status": "Success" })
});

//Posts
app.post('/Users/Registration', async (req, res) => { //1
    result = await SQL_post.RegistrateUser(req.body);
    res.send(result);
});
app.post('/Users/Update', async (req, res) => { //1
    result = await SQL_post.UpdateUser(req.body);
    res.send(result);
});
app.post('/Criteria/Value/Update', async (req, res) => { //1
    result = await SQL_post.UpdateValue(req.body);
    res.send(result);
});
app.post('/Criteria/Value/Add', async (req, res) => { //1
    result = await SQL_post.AddValue(req.body);
    res.send(result);
});
;
app.post('/Criteria/Update', async (req, res) => { //1
    result = await SQL_post.UpdateCriteria(req.body);
    res.send(result);
});


app.use(fileUpload());
app.post('/UploadExcell', async (req, res) => { //1
    if (req.files && Object.keys(req.files).length !== 0) {
        uploadedFile = await req.files.test;
        nameParts = uploadedFile.name.split('.')

        if (nameParts[nameParts.length - 1] === 'xlsx') {
            UpPath = __dirname + '/uploads/test.xlsx';

            uploadedFile.mv(UpPath, async function (err) {
                if (err) {

                } else {
                    result = await SQL_post.UploadExcell(req.query);
                    //console.log(result)
                    if (result["Error"] === undefined) {
                        //console.log("Ok")
                        res.sendStatus(200);
                    } else {
                        res.send(result);
                        //res.sendStatus(406);
                    }
                };
            })
        } else {

        }
    } else {

    };

});

app.listen(port, () => {
    console.log(`Example app listening on port ${port}`)
})
app.use(function(req, res, next) {
  next(createError(404));
});

// error handler
app.use(function(err, req, res, next) {
  // set locals, only providing error in development
  res.locals.message = err.message;
  res.locals.error = req.app.get('env') === 'development' ? err : {};

  // render the error page
  res.status(err.status || 500);
  res.render('error');
});


module.exports = app;
