if not exists(SELECT name  
     FROM master.sys.server_principals
     WHERE name = 'test_user')
BEGIN
	CREATE LOGIN test_user 
		WITH PASSWORD    = N'test_user_password',
		CHECK_POLICY     = OFF,
		CHECK_EXPIRATION = OFF;
	EXEC sp_addsrvrolemember 
		@loginame = N'test_user', 
		@rolename = N'sysadmin';
	PRINT 'created test_user user'
END
ELSE
PRINT 'user test_user exists'